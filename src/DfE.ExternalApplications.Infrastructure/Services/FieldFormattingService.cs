using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using System.Text.Json;

namespace DfE.ExternalApplications.Infrastructure.Services
{
    /// <summary>
    /// Implementation of field formatting service for display purposes
    /// </summary>
    public class FieldFormattingService : IFieldFormattingService
    {
        private readonly IComplexFieldConfigurationService _complexFieldConfigurationService;

        public FieldFormattingService(IComplexFieldConfigurationService complexFieldConfigurationService)
        {
            _complexFieldConfigurationService = complexFieldConfigurationService;
        }

        public string GetFieldValue(string fieldId, Dictionary<string, object> formData)
        {
            if (formData.TryGetValue(fieldId, out var value))
            {
                if (value == null)
                {
                    return string.Empty;
                }

                // If it's already a string, return it
                if (value is string stringValue)
                {
                    return stringValue;
                }

                // If it's an object (like from autocomplete), serialize it to JSON
                try
                {
                    return JsonSerializer.Serialize(value);
                }
                catch
                {
                    return value.ToString() ?? string.Empty;
                }
            }
            return string.Empty;
        }

        public string GetFormattedFieldValue(string fieldId, Dictionary<string, object> formData)
        {
            var fieldValue = GetFieldValue(fieldId, formData);
            
            if (string.IsNullOrEmpty(fieldValue))
            {
                return string.Empty;
            }

            // Try to format as autocomplete data if it looks like JSON
            if (fieldValue.StartsWith("{") || fieldValue.StartsWith("["))
            {
                if (LooksLikeUploadData(fieldValue))
                {
                    return FormatUploadValue(fieldValue);
                }

                return FormatAutocompleteValue(fieldValue);
            }

            return fieldValue;
        }

        public List<string> GetFormattedFieldValues(string fieldId, Dictionary<string, object> formData)
        {
            var fieldValue = GetFieldValue(fieldId, formData);
            
            if (string.IsNullOrEmpty(fieldValue))
            {
                return new List<string>();
            }

            if (fieldValue.StartsWith("{") || fieldValue.StartsWith("["))
            {
                if (LooksLikeUploadData(fieldValue))
                {
                    return FormatUploadValuesList(fieldValue);
                }

                return FormatAutocompleteValuesList(fieldValue);
            }

            return new List<string> { fieldValue };
        }

        public string GetFieldItemLabel(string fieldId, FormTemplate template)
        {
            // Find the field in the template
            var field = template?.TaskGroups?
                .SelectMany(g => g.Tasks)
                .SelectMany(t => t.Pages)
                .SelectMany(p => p.Fields)
                .FirstOrDefault(f => f.FieldId == fieldId);

            if (field?.ComplexField != null && !string.IsNullOrEmpty(field.ComplexField.Id))
            {
                // Get configuration from the service to determine the appropriate label
                var configuration = _complexFieldConfigurationService.GetConfiguration(field.ComplexField.Id);
                return configuration.Label;
            }

            // Default label if not found in properties
            return "Item";
        }

        public bool IsFieldAllowMultiple(string fieldId, FormTemplate template)
        {
            // Find the field in the template
            var field = template?.TaskGroups?
                .SelectMany(g => g.Tasks)
                .SelectMany(t => t.Pages)
                .SelectMany(p => p.Fields)
                .FirstOrDefault(f => f.FieldId == fieldId);

            if (field?.ComplexField != null && !string.IsNullOrEmpty(field.ComplexField.Id))
            {
                // Get configuration from the service
                var configuration = _complexFieldConfigurationService.GetConfiguration(field.ComplexField.Id);
                return configuration.AllowMultiple;
            }

            return false; // Default to single selection
        }

        public bool HasFieldValue(string fieldId, Dictionary<string, object> formData)
        {
            var value = GetFieldValue(fieldId, formData);
            return !string.IsNullOrWhiteSpace(value);
        }

        private string FormatAutocompleteValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            try
            {
                using (var doc = JsonDocument.Parse(value))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var displayValues = new List<string>();
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            displayValues.Add(FormatSingleAutocompleteValue(element));
                        }
                        return string.Join("<br />", displayValues);
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        return FormatSingleAutocompleteValue(doc.RootElement);
                    }
                }
            }
            catch
            {
                // If not JSON, return as is
            }

            return value;
        }

        private List<string> FormatAutocompleteValuesList(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return new List<string>();
            }

            try
            {
                using (var doc = JsonDocument.Parse(value))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var displayValues = new List<string>();
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            displayValues.Add(FormatSingleAutocompleteValue(element));
                        }
                        return displayValues;
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        return new List<string> { FormatSingleAutocompleteValue(doc.RootElement) };
                    }
                }
            }
            catch
            {
                // If not JSON, return as single item
            }

            return new List<string> { value };
        }

        private string FormatSingleAutocompleteValue(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                string name = "";
                string ukprn = "";
                string chNumber = "";

                if (element.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String)
                {
                    name = nameProperty.GetString() ?? "";
                }

                if (element.TryGetProperty("ukprn", out var ukprnProperty))
                {
                    if (ukprnProperty.ValueKind == JsonValueKind.String)
                    {
                        ukprn = ukprnProperty.GetString() ?? "";
                    }
                    else if (ukprnProperty.ValueKind == JsonValueKind.Number)
                    {
                        ukprn = ukprnProperty.GetInt64().ToString();
                    }
                }

                if (element.TryGetProperty("companiesHouseNumber", out var chProperty))
                {
                    if (chProperty.ValueKind == JsonValueKind.String)
                    {
                        chNumber = chProperty.GetString() ?? "";
                    }
                }

                var parts = new List<string>();
                if (!string.IsNullOrEmpty(ukprn)) parts.Add($"UKPRN: {System.Web.HttpUtility.HtmlEncode(ukprn)}");
                if (!string.IsNullOrEmpty(chNumber)) parts.Add($"Companies House: {System.Web.HttpUtility.HtmlEncode(chNumber)}");

                if (!string.IsNullOrEmpty(name) && parts.Count > 0)
                {
                    return $"{System.Web.HttpUtility.HtmlEncode(name)} - {string.Join(" - ", parts)}";
                }
                else if (!string.IsNullOrEmpty(name))
                {
                    return System.Web.HttpUtility.HtmlEncode(name);
                }
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                return System.Web.HttpUtility.HtmlEncode(element.GetString() ?? "");
            }

            return System.Web.HttpUtility.HtmlEncode(element.ToString());
        }

        private bool LooksLikeUploadData(string value)
        {
            try
            {
                using var doc = JsonDocument.Parse(value);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var first = doc.RootElement[0];
                    // Check for upload file properties - either OriginalFileName or Name with file-like properties
                    return first.ValueKind == JsonValueKind.Object && 
                           (first.TryGetProperty("originalFileName", out _) || 
                            (first.TryGetProperty("name", out _) && 
                             (first.TryGetProperty("fileSize", out _) || first.TryGetProperty("id", out _))));
                }
            }
            catch
            {
                // ignore parse errors
            }

            return false;
        }

        private string FormatUploadValue(string value)
        {
            try
            {
                using var doc = JsonDocument.Parse(value);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var names = doc.RootElement.EnumerateArray()
                        .Select(e => 
                        {
                            // Try OriginalFileName first, then fall back to Name if OriginalFileName doesn't exist
                            if (e.TryGetProperty("originalFileName", out var originalFileName))
                                return originalFileName.GetString() ?? string.Empty;
                            if (e.TryGetProperty("name", out var name))
                                return name.GetString() ?? string.Empty;
                            return string.Empty;
                        })
                        .Where(n => !string.IsNullOrEmpty(n));
                    return string.Join("<br />", names);
                }
            }
            catch
            {
                // ignore and return raw value
            }
            return value;
        }

        private List<string> FormatUploadValuesList(string value)
        {
            try
            {
                using var doc = JsonDocument.Parse(value);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var names = doc.RootElement.EnumerateArray()
                        .Select(e => 
                        {
                            // Try OriginalFileName first, then fall back to Name if OriginalFileName doesn't exist
                            if (e.TryGetProperty("originalFileName", out var originalFileName))
                                return originalFileName.GetString() ?? string.Empty;
                            if (e.TryGetProperty("name", out var name))
                                return name.GetString() ?? string.Empty;
                            return string.Empty;
                        })
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList();
                    return names;
                }
            }
            catch
            {
                // ignore
            }
            return new List<string> { value };
        }
    }
} 