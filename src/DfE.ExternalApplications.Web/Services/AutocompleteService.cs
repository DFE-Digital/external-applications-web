using System.Text.Json;
using System.Linq;
using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;

namespace DfE.ExternalApplications.Web.Services
{
    public class AutocompleteService : IAutocompleteService
    {
        private readonly HttpClient _httpClient;
        private readonly IComplexFieldConfigurationService _complexFieldConfigurationService;
        private readonly ILogger<AutocompleteService> _logger;

        public AutocompleteService(
            HttpClient httpClient, 
            IComplexFieldConfigurationService complexFieldConfigurationService,
            ILogger<AutocompleteService> logger)
        {
            _httpClient = httpClient;
            _complexFieldConfigurationService = complexFieldConfigurationService;
            _logger = logger;
        }

        public async Task<List<object>> SearchAsync(string complexFieldId, string query)
        {
            _logger.LogInformation("AutocompleteService.SearchAsync called with complexFieldId: {ComplexFieldId}, query: {Query}", complexFieldId, query);
            
            if (string.IsNullOrWhiteSpace(query))
            {
                _logger.LogDebug("Query is empty, returning empty results");
                return new List<object>();
            }

            var configuration = _complexFieldConfigurationService.GetConfiguration(complexFieldId);
            _logger.LogInformation("Retrieved configuration for complexFieldId: {ComplexFieldId}, ApiEndpoint: {ApiEndpoint}", complexFieldId, configuration.ApiEndpoint);
            
            if (string.IsNullOrWhiteSpace(configuration.ApiEndpoint))
            {
                _logger.LogWarning("No API endpoint configured for complex field: {ComplexFieldId}", complexFieldId);
                return new List<object>();
            }

            if (query.Length < configuration.MinLength)
            {
                _logger.LogDebug("Query too short for complex field {ComplexFieldId}: {QueryLength} < {MinLength}", 
                    complexFieldId, query.Length, configuration.MinLength);
                return new List<object>();
            }

            try
            {
                // Build the request URL with query parameter
                var requestUrl = BuildRequestUrl(configuration.ApiEndpoint, query);
                
                _logger.LogInformation("Making autocomplete request to: {RequestUrl} for complex field: {ComplexFieldId}", requestUrl, complexFieldId);

                // Create the request
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                
                // Add authentication headers if configured
                AddAuthenticationHeaders(request, configuration);

                // Make the API call
                var response = await _httpClient.SendAsync(request);
                
                _logger.LogDebug("HTTP response status: {StatusCode} for complex field: {ComplexFieldId}", response.StatusCode, complexFieldId);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Autocomplete API call failed with status {StatusCode} for complex field: {ComplexFieldId}. Response: {ErrorContent}", 
                        response.StatusCode, complexFieldId, errorContent);
                    return new List<object>();
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Raw JSON response for complex field {ComplexFieldId}: {JsonResponse}", complexFieldId, jsonResponse);
                
                var results = ParseResponse(jsonResponse);

                // Normalise and de-duplicate results before sorting
                results = DeduplicateAndNormalise(results);

                // Sort results with prefix priority, then alphabetical
                var sortedResults = SortResultsByPrefixThenAlpha(results, query);

                _logger.LogDebug("Found {Count} results for query: {Query} from complex field: {ComplexFieldId}", 
                    sortedResults.Count, query, complexFieldId);
                return sortedResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling autocomplete API for complex field: {ComplexFieldId}, query: {Query}", complexFieldId, query);
                return new List<object>();
            }
        }
       
        private string BuildRequestUrl(string endpoint, string query)
        {
            var encodedQuery = Uri.EscapeDataString(query);
            
            if (endpoint.Contains("{0}"))
            {
                return endpoint.Replace("{0}", encodedQuery);
            }
            
            var separator = endpoint.Contains("?") ? "&" : "?";
            return $"{endpoint}{separator}q={encodedQuery}";
        }

        private void AddAuthenticationHeaders(HttpRequestMessage request, ComplexFieldConfiguration configuration)
        {
            if (!string.IsNullOrEmpty(configuration.ApiKey))
            {
                request.Headers.Add("ApiKey", configuration.ApiKey);
                _logger.LogDebug("Added API key authentication header for complex field");
            }
        }

        private List<object> ParseResponse(string jsonResponse)
        {
            var results = new List<object>();
            
            try
            {
                var apiData = JsonSerializer.Deserialize<JsonElement>(jsonResponse);
                
                // Handle direct array response
                if (apiData.ValueKind == JsonValueKind.Array)
                {
                    ExtractObjectsFromArray(apiData, results);
                }
                // Handle object with nested array
                else if (apiData.ValueKind == JsonValueKind.Object)
                {
                    // Try common property names for arrays
                    var arrayProperties = new[] { "data", "results", "items", "values" };
                    
                    foreach (var propertyName in arrayProperties)
                    {
                        if (apiData.TryGetProperty(propertyName, out var arrayProperty) && arrayProperty.ValueKind == JsonValueKind.Array)
                        {
                            ExtractObjectsFromArray(arrayProperty, results);
                            break;
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse JSON response for autocomplete");
            }
            
            return results;
        }

        private void ExtractObjectsFromArray(JsonElement arrayElement, List<object> results)
        {
            foreach (var item in arrayElement.EnumerateArray())
            {
                var displayValue = ExtractDisplayValue(item);
                if (displayValue != null && !displayValue.Equals(string.Empty))
                {
                    results.Add(displayValue);
                }
            }
        }

        private object ExtractDisplayValue(JsonElement item)
        {
            // If it's already a string, use it directly
            if (item.ValueKind == JsonValueKind.String)
            {
                return item.GetString() ?? string.Empty;
            }
            
            // If it's an object, try to extract structured data
            if (item.ValueKind == JsonValueKind.Object)
            {
                // For trust data, try to extract both name and URN
                var result = new Dictionary<string, object>();
                
                // Try to get the display name
                var displayProperties = new[] { "name", "title", "label", "value", "displayName", "groupName", "text" };
                string displayName = null;
                
                foreach (var propertyName in displayProperties)
                {
                    if (item.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
                    {
                        var value = property.GetString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            displayName = value;
                            result["name"] = value;
                            break;
                        }
                    }
                }
                
                // Try to get UKPRN or other identifier fields (support common casing variants)
                var identifierProperties = new[] { "ukprn", "id", "urn", "companiesHouseNumber", "companieshousenumber", "companies_house_number", "code" };
                foreach (var propertyName in identifierProperties)
                {
                    if (item.TryGetProperty(propertyName, out var property))
                    {
                        if (property.ValueKind == JsonValueKind.String)
                        {
                            var value = property.GetString();
                            if (!string.IsNullOrEmpty(value))
                            {
                                result[propertyName] = value;
                            }
                        }
                        else if (property.ValueKind == JsonValueKind.Number)
                        {
                            result[propertyName] = property.GetInt64().ToString();
                        }
                    }
                }
                
                // If we found a display name and at least one other field, return the object
                if (!string.IsNullOrEmpty(displayName) && result.Count > 1)
                {
                    return result;
                }
                
                // Otherwise return just the display name
                if (!string.IsNullOrEmpty(displayName))
                {
                    return displayName;
                }
            }
            
            return string.Empty;
        }

        /// <summary>
        /// Sorts autocomplete results alphabetically by their display name
        /// </summary>
        /// <param name="results">The list of results to sort</param>
        /// <returns>A new list with results sorted alphabetically</returns>
        private List<object> SortResultsAlphabetically(List<object> results)
        {
            return results.OrderBy(result => GetDisplayTextForSorting(result), StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Extracts the display text from a result object for sorting purposes
        /// </summary>
        /// <param name="result">The result object (either string or Dictionary)</param>
        /// <returns>The display text to use for sorting</returns>
        private string GetDisplayTextForSorting(object result)
        {
            if (result is string stringResult)
            {
                return stringResult;
            }
            
            if (result is Dictionary<string, object> dictResult)
            {
                // Try to get the display name from common properties
                var displayProperties = new[] { "name", "title", "label", "value", "displayName", "groupName", "text" };
                
                foreach (var propertyName in displayProperties)
                {
                    if (dictResult.TryGetValue(propertyName, out var value) && value != null)
                    {
                        return value.ToString() ?? string.Empty;
                    }
                }
            }
            
            // Fallback to string representation
            return result?.ToString() ?? string.Empty;
        }

        // Sort results so items starting with the query term appear first, then alphabetical
        private List<object> SortResultsByPrefixThenAlpha(List<object> results, string query)
        {
            var q = (query ?? string.Empty).Trim();
            if (q.Length == 0)
            {
                return SortResultsAlphabetically(results);
            }

            return results
                .OrderBy(r =>
                {
                    var text = GetDisplayTextForSorting(r);
                    return text.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                })
                .ThenBy(r => GetDisplayTextForSorting(r), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Normalise casing variants and de-duplicate results by stable keys, merging properties
        private List<object> DeduplicateAndNormalise(List<object> results)
        {
            var mergedByKey = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

            static string? GetFirstNonEmpty(Dictionary<string, object> d, params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (d.TryGetValue(k, out var v) && v != null)
                    {
                        var s = v.ToString();
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                }
                return null;
            }

            foreach (var item in results)
            {
                if (item is Dictionary<string, object> dict)
                {
                    // Canonicalise companies house number to companiesHouseNumber
                    var ch = GetFirstNonEmpty(dict, "companiesHouseNumber", "companieshousenumber", "companies_house_number");
                    if (!string.IsNullOrWhiteSpace(ch))
                    {
                        dict["companiesHouseNumber"] = ch;
                    }

                    // Build a stable dedupe key (prefer ukprn, then companiesHouseNumber, urn, id, else name)
                    var key = GetFirstNonEmpty(dict, "ukprn")
                              ?? GetFirstNonEmpty(dict, "companiesHouseNumber")
                              ?? GetFirstNonEmpty(dict, "urn")
                              ?? GetFirstNonEmpty(dict, "id")
                              ?? (GetFirstNonEmpty(dict, "name") is string n && !string.IsNullOrWhiteSpace(n) ? $"name:{n}" : null);

                    if (string.IsNullOrWhiteSpace(key))
                    {
                        // No usable key: keep as unique entry
                        mergedByKey[Guid.NewGuid().ToString()] = dict;
                        continue;
                    }

                    if (!mergedByKey.TryGetValue(key, out var existing))
                    {
                        mergedByKey[key] = dict;
                    }
                    else
                    {
                        // Merge non-empty properties into the existing record, preferring already-populated fields
                        foreach (var kv in dict)
                        {
                            var val = kv.Value?.ToString();
                            if (string.IsNullOrWhiteSpace(val)) continue;
                            if (!existing.TryGetValue(kv.Key, out var existingVal) || string.IsNullOrWhiteSpace(existingVal?.ToString()))
                            {
                                existing[kv.Key] = kv.Value!;
                            }
                        }
                    }
                }
                else if (item is string s)
                {
                    var key = $"str:{s}";
                    if (!mergedByKey.ContainsKey(key))
                    {
                        mergedByKey[key] = new Dictionary<string, object> { { "name", s } };
                    }
                }
            }

            // Convert back: if an entry is just a name, return string; otherwise return the object
            var normalised = new List<object>();
            foreach (var kvp in mergedByKey)
            {
                var d = kvp.Value;
                if (d.Count == 1 && d.ContainsKey("name"))
                {
                    normalised.Add(d["name"]?.ToString() ?? string.Empty);
                }
                else
                {
                    normalised.Add(d);
                }
            }

            return normalised;
        }
    }
} 