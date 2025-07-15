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

            // Try to format as autocomplete data if it looks like JSON
            if (fieldValue.StartsWith("{") || fieldValue.StartsWith("["))
            {
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

            if (field?.ComplexField != null)
            {
                try
                {
                    var complexField = JsonSerializer.Deserialize<Dictionary<string, object>>(field.ComplexField);
                    if (complexField?.ContainsKey("properties") == true)
                    {
                        var properties = JsonSerializer.Deserialize<Dictionary<string, object>>(complexField["properties"].ToString());
                        if (properties?.ContainsKey("label") == true)
                        {
                            return properties["label"].ToString();
                        }
                    }
                }
                catch
                {
                    // If parsing fails, return default
                }
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

            if (field?.ComplexField != null)
            {
                try
                {
                    var complexField = JsonSerializer.Deserialize<Dictionary<string, object>>(field.ComplexField);
                    if (complexField?.ContainsKey("properties") == true)
                    {
                        var properties = JsonSerializer.Deserialize<Dictionary<string, object>>(complexField["properties"].ToString());
                        if (properties?.ContainsKey("allowMultiple") == true)
                        {
                            return bool.Parse(properties["allowMultiple"].ToString());
                        }
                    }
                }
                catch
                {
                    // If parsing fails, return default
                }
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

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(ukprn))
                {
                    return $"{System.Web.HttpUtility.HtmlEncode(name)} (UKPRN: {System.Web.HttpUtility.HtmlEncode(ukprn)})";
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
    }
} 