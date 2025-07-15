using System.Text.Json;
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
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<object>();
            }

            var configuration = _complexFieldConfigurationService.GetConfiguration(complexFieldId);
            
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
                
                _logger.LogDebug("Making autocomplete request to: {RequestUrl} for complex field: {ComplexFieldId}", requestUrl, complexFieldId);

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
                var results = ParseResponse(jsonResponse);

                _logger.LogDebug("Found {Count} results for query: {Query} from complex field: {ComplexFieldId}", 
                    results.Count, query, complexFieldId);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling autocomplete API for complex field: {ComplexFieldId}, query: {Query}", complexFieldId, query);
                return new List<object>();
            }
        }

        //// Legacy method for backward compatibility
        //public async Task<List<object>> SearchAsync(string endpoint, string query)
        //{
        //    _logger.LogWarning("Using legacy SearchAsync method with endpoint. Consider using complex field ID instead.");
            
        //    if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
        //    {
        //        return new List<object>();
        //    }

        //    if (string.IsNullOrWhiteSpace(endpoint))
        //    {
        //        _logger.LogWarning("No endpoint provided for autocomplete search");
        //        return new List<object>();
        //    }

        //    try
        //    {
        //        // Build the request URL with query parameter
        //        var requestUrl = BuildRequestUrl(endpoint, query);
                
        //        _logger.LogDebug("Making autocomplete request to: {RequestUrl}", requestUrl);

        //        // Create the request
        //        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                
        //        // Add authentication headers if configured for this endpoint
        //        AddAuthenticationHeaders(request, endpoint);

        //        // Make the API call
        //        var response = await _httpClient.SendAsync(request);
                
        //        _logger.LogDebug("HTTP response status: {StatusCode} for endpoint: {Endpoint}", response.StatusCode, endpoint);
                
        //        if (!response.IsSuccessStatusCode)
        //        {
        //            var errorContent = await response.Content.ReadAsStringAsync();
        //            _logger.LogWarning("Autocomplete API call failed with status {StatusCode} for endpoint: {Endpoint}. Response: {ErrorContent}", response.StatusCode, endpoint, errorContent);
        //            return new List<object>();
        //        }

        //        var jsonResponse = await response.Content.ReadAsStringAsync();
        //        var results = ParseResponse(jsonResponse);

        //        _logger.LogDebug("Found {Count} results for query: {Query} from endpoint: {Endpoint}", results.Count, query, endpoint);
        //        return results;
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error calling autocomplete API for endpoint: {Endpoint}, query: {Query}", endpoint, query);
        //        return new List<object>();
        //    }
        //}

        private string BuildRequestUrl(string endpoint, string query)
        {
            var encodedQuery = Uri.EscapeDataString(query);
            
            // If endpoint contains {0} placeholders, replace them with the query
            if (endpoint.Contains("{0}"))
            {
                return endpoint.Split("{0}").Aggregate((current, next) => current + encodedQuery + next);
            }
            
            // Otherwise, append as a query parameter
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

        private void AddAuthenticationHeaders(HttpRequestMessage request, string endpoint)
        {
            // For legacy endpoint-based authentication, we'll use a default configuration
            // This maintains backward compatibility but new implementations should use complex field IDs
            _logger.LogDebug("Using legacy authentication for endpoint: {Endpoint}", endpoint);
            
            // Could add other authentication methods here based on configuration
            // e.g., Bearer tokens, Basic auth, etc.
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
                
                // Try to get UKPRN or other identifier fields
                var identifierProperties = new[] { "ukprn", "id", "urn", "companiesHouseNumber", "code" };
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
    }
} 