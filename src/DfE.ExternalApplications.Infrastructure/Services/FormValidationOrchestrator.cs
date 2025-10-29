using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Globalization;
using System.ComponentModel.DataAnnotations;
using Task = DfE.ExternalApplications.Domain.Models.Task;

namespace DfE.ExternalApplications.Infrastructure.Services
{
    /// <summary>
    /// Implementation of the form validation orchestrator that handles validation logic
    /// </summary>
    public class FormValidationOrchestrator : IFormValidationOrchestrator
    {
        private readonly ILogger<FormValidationOrchestrator> _logger;
        private readonly IConditionalLogicEngine _conditionalLogicEngine;
        private readonly IFieldRequirementService _fieldRequirementService;

        public FormValidationOrchestrator(
            ILogger<FormValidationOrchestrator> logger, 
            IConditionalLogicEngine conditionalLogicEngine,
            IFieldRequirementService fieldRequirementService)
        {
            _logger = logger;
            _conditionalLogicEngine = conditionalLogicEngine;
            _fieldRequirementService = fieldRequirementService;
        }

        /// <summary>
        /// Validates a single page
        /// </summary>
        /// <param name="page">The page to validate</param>
        /// <param name="data">The form data</param>
        /// <param name="modelState">The model state to add errors to</param>
        /// <param name="template">Optional template for field requirement policy</param>
        /// <returns>True if validation passes</returns>
        public bool ValidatePage(Page page, Dictionary<string, object> data, ModelStateDictionary modelState, FormTemplate? template = null)
        {
            if (page?.Fields == null)
            {
                return true;
            }

            var isValid = true;
            foreach (var field in page.Fields)
            {
                var key = field.FieldId;
                data.TryGetValue(key, out var rawValue);
                var value = rawValue?.ToString() ?? string.Empty;

                if (!ValidateField(field, value, data, modelState, key, template))
                {
                    isValid = false;
                }
            }

            return isValid;
        }

        /// <summary>
        /// Validates a single task
        /// </summary>
        /// <param name="task">The task to validate</param>
        /// <param name="data">The form data</param>
        /// <param name="modelState">The model state to add errors to</param>
        /// <param name="template">Optional template for field requirement policy</param>
        /// <returns>True if validation passes</returns>
        public bool ValidateTask(Task task, Dictionary<string, object> data, ModelStateDictionary modelState, FormTemplate? template = null)
        {
            if (task?.Pages == null)
            {
                return true;
            }

            var isValid = true;
            foreach (var page in task.Pages)
            {
                if (!ValidatePage(page, data, modelState, template))
                {
                    isValid = false;
                }
            }

            return isValid;
        }

        /// <summary>
        /// Validates the entire application
        /// </summary>
        /// <param name="template">The form template</param>
        /// <param name="data">The form data</param>
        /// <param name="modelState">The model state to add errors to</param>
        /// <returns>True if validation passes</returns>
        public bool ValidateApplication(FormTemplate template, Dictionary<string, object> data, ModelStateDictionary modelState)
        {
            if (template?.TaskGroups == null)
            {
                return true;
            }

            var isValid = true;
            foreach (var group in template.TaskGroups)
            {
                foreach (var task in group.Tasks)
                {
                    if (!ValidateTask(task, data, modelState))
                    {
                        isValid = false;
                    }
                }
            }

            return isValid;
        }

        /// <summary>
        /// Validates a single field
        /// </summary>
        /// <param name="field">The field to validate</param>
        /// <param name="value">The field value</param>
        /// <param name="modelState">The model state to add errors to</param>
        /// <param name="fieldKey">The field key for model state</param>
        /// <returns>True if validation passes</returns>
        public bool ValidateField(Field field, object value, ModelStateDictionary modelState, string fieldKey)
        {
            // Call the overloaded method with null data and template for backward compatibility
            return ValidateField(field, value, null, modelState, fieldKey, null);
        }

        /// <summary>
        /// Validates a single field with full form data context for conditional validation
        /// </summary>
        /// <param name="field">The field to validate</param>
        /// <param name="value">The field value</param>
        /// <param name="formData">The complete form data for conditional evaluation</param>
        /// <param name="modelState">The model state to add errors to</param>
        /// <param name="fieldKey">The field key for model state</param>
        /// <returns>True if validation passes</returns>
        public bool ValidateField(Field field, object value, Dictionary<string, object>? formData, ModelStateDictionary modelState, string fieldKey)
        {
            return ValidateField(field, value, formData, modelState, fieldKey, null);
        }

        /// <summary>
        /// Validates a single field with full form data context, conditional validation, and template-based requirement policy
        /// </summary>
        /// <param name="field">The field to validate</param>
        /// <param name="value">The field value</param>
        /// <param name="formData">The complete form data for conditional evaluation</param>
        /// <param name="modelState">The model state to add errors to</param>
        /// <param name="fieldKey">The field key for model state</param>
        /// <param name="template">The template containing the default field requirement policy</param>
        /// <returns>True if validation passes</returns>
        public bool ValidateField(Field field, object value, Dictionary<string, object>? formData, ModelStateDictionary modelState, string fieldKey, FormTemplate? template)
        {
            var stringValue = value?.ToString() ?? string.Empty;
            var isValid = true;

            // Special handling for complex fields (upload, autocomplete, etc.)
            if (field.Type == "complexField" && field.ComplexField != null)
            {
                // Pass template to complex field validation so it can check global required policy
                return ValidateComplexField(field, value, formData, modelState, fieldKey, template);
            }

            // Check if field is required based on template policy (before explicit validation rules)
            // This check is for standard fields only - complex fields are handled above
            // Skip this check if there's an explicit required validation rule (to avoid duplicate errors)
            bool hasExplicitRequiredRule = field.Validations?.Any(v => 
                string.Equals(v.Type, "required", StringComparison.OrdinalIgnoreCase)) ?? false;
            
            if (template != null && !hasExplicitRequiredRule && _fieldRequirementService.IsFieldRequired(field, template))
            {
                var hasExplicitRequired = field.Validations?.Any(v => string.Equals(v.Type, "required", StringComparison.OrdinalIgnoreCase)) == true;
                if (!hasExplicitRequired && _fieldRequirementService.IsFieldRequired(field, template))
                {
                    if (string.IsNullOrWhiteSpace(stringValue))
                    {
                        var fieldLabel = field.Label?.Value ?? field.FieldId;
                        modelState.AddModelError(fieldKey, $"{fieldLabel} is required");
                        isValid = false;
                    }
                }
            }

            // Automatic date validation even when no explicit rules are provided
            if (string.Equals(field.Type, "date", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(stringValue))
                {
                    // Detect missing date parts (day/month/year) from the composed value
                    // We compose values as "YYYY-M-D" when parts are incomplete or invalid
                    var fieldLabel = field.Label?.Value ?? field.FieldId;
                    var missingParts = false;

                    if (stringValue.Contains('-'))
                    {
                        var bits = stringValue.Split('-', StringSplitOptions.TrimEntries);
                        if (bits.Length == 3)
                        {
                            missingParts = string.IsNullOrWhiteSpace(bits[0]) || string.IsNullOrWhiteSpace(bits[1]) || string.IsNullOrWhiteSpace(bits[2]);
                        }
                    }

                    if (missingParts)
                    {
                        modelState.AddModelError(fieldKey, $"{fieldLabel} must include a day, month and year");
                        isValid = false;
                    }
                    else if (!DateTime.TryParseExact(stringValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    {
                        // All parts present and numeric but not a real calendar date
                        modelState.AddModelError(fieldKey, $"{fieldLabel} must be a real date");
                        isValid = false;
                    }
                }
            }

            // Automatic email validation
            if (string.Equals(field.Type, "email", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(stringValue))
                {
                    var emailAttr = new EmailAddressAttribute();
                    if (!emailAttr.IsValid(stringValue))
                    {
                        modelState.AddModelError(fieldKey, "Enter an email address in the correct format, for example, name@example.com");
                        isValid = false;
                    }
                }
            }

            if (field?.Validations != null)
            {
                foreach (var rule in field.Validations)
                {
                    // Check if this is a conditional validation rule
                    if (rule.Condition != null)
                    {
                        if (formData == null)
                        {
                            _logger.LogWarning("Conditional validation rule found for field '{FieldId}' but no form data provided for evaluation. Skipping rule.", field.FieldId);
                            continue;
                        }

                        try
                        {
                            // Evaluate the condition using the conditional logic engine
                            bool conditionMet = _conditionalLogicEngine.EvaluateCondition(rule.Condition, formData);
                            
                            if (!conditionMet)
                            {
                                // Condition not met, skip this validation rule
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error evaluating conditional validation rule for field '{FieldId}'. Skipping rule.", field.FieldId);
                            continue;
                        }
                    }

                    switch (rule.Type)
                    {
                        case "required":
                            if (string.IsNullOrWhiteSpace(stringValue))
                            {
                                modelState.AddModelError(fieldKey, rule.Message);
                                isValid = false;
                            }
                            break;
                        case "regex":
                            var pattern = rule.Rule?.ToString();
                            if (!string.IsNullOrWhiteSpace(stringValue) && !string.IsNullOrEmpty(pattern))
                            {
                                var regexMatch = Regex.IsMatch(stringValue, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200));
                                if (!regexMatch)
                                {
                                    modelState.AddModelError(fieldKey, rule.Message);
                                    isValid = false;
                                }
                            }
                            break;
                        case "maxLength":
                            var maxLengthStr = rule.Rule?.ToString();
                            if (!string.IsNullOrEmpty(maxLengthStr) && int.TryParse(maxLengthStr, out var maxLength))
                            {
                                if (stringValue.Length > maxLength)
                                {
                                    modelState.AddModelError(fieldKey, rule.Message);
                                    isValid = false;
                                }
                            }
                            break;
                        default:
                            _logger.LogWarning("Unknown validation rule type: {RuleType} for field '{FieldKey}'", rule.Type, fieldKey);
                            break;
                    }
                }
            }

            return isValid;
        }

        #region Complex Field Validation

        /// <summary>
        /// Validates a complex field (upload, autocomplete, etc.)
        /// </summary>
        /// <param name="field">The complex field to validate</param>
        /// <param name="value">The field value</param>
        /// <param name="formData">The complete form data for conditional evaluation</param>
        /// <param name="modelState">The model state to add errors to</param>
        /// <param name="fieldKey">The field key for model state</param>
        /// <param name="template">The template containing the default field requirement policy</param>
        /// <returns>True if validation passes</returns>
        private bool ValidateComplexField(Field field, object? value, Dictionary<string, object>? formData, ModelStateDictionary modelState, string fieldKey, FormTemplate? template = null)
        {
            var stringValue = value?.ToString() ?? string.Empty;
            var isValid = true;
            
            // Determine if this is an upload field
            bool isUploadField = field.ComplexField!.Id.Contains("Upload", StringComparison.OrdinalIgnoreCase);

            // Check if field is required based on template policy (when no explicit validation rules exist)
            // This ensures upload fields respect the global required policy
            // Skip this check if there's an explicit required validation rule (to avoid duplicate errors)
            bool hasExplicitRequiredRule = field.Validations?.Any(v => 
                string.Equals(v.Type, "required", StringComparison.OrdinalIgnoreCase)) ?? false;
            
            if (template != null && !hasExplicitRequiredRule && _fieldRequirementService.IsFieldRequired(field, template))
            {
                if (isUploadField)
                {
                    // For upload fields, check if files are uploaded
                    bool hasFiles = HasUploadedFiles(stringValue);
                    if (!hasFiles)
                    {
                        var fieldLabel = field.Label?.Value ?? field.FieldId;
                        modelState.AddModelError(fieldKey, $"{fieldLabel} is required");
                        isValid = false;
                    }
                }
                else
                {
                    // For other complex fields (autocomplete), check if value is empty
                    if (string.IsNullOrWhiteSpace(stringValue))
                    {
                        var fieldLabel = field.Label?.Value ?? field.FieldId;
                        modelState.AddModelError(fieldKey, $"{fieldLabel} is required");
                        isValid = false;
                    }
                }
            }

            // If no explicit validation rules, we're done (global policy check above is sufficient)
            if (field.Validations == null)
            {
                return isValid;
            }

            foreach (var rule in field.Validations)
            {
                // Check if this is a conditional validation rule
                if (rule.Condition != null)
                {
                    if (formData == null)
                    {
                        _logger.LogWarning("Conditional validation rule found for complex field '{FieldId}' but no form data provided for evaluation. Skipping rule.", field.FieldId);
                        continue;
                    }

                    try
                    {
                        var conditionResult = _conditionalLogicEngine.EvaluateCondition(rule.Condition, formData);
                        
                        if (!conditionResult)
                        {
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error evaluating conditional validation rule for complex field '{FieldId}'. Skipping rule.", field.FieldId);
                        continue;
                    }
                }

                switch (rule.Type.ToLowerInvariant())
                {
                    case "required":
                        if (isUploadField)
                        {
                            // For upload fields, check if files are uploaded
                            bool hasFiles = HasUploadedFiles(stringValue);
                            if (!hasFiles)
                            {
                                // Use a more appropriate error message for upload fields if the template message is clearly wrong
                                var errorMessage = rule.Message;
                                if (errorMessage.Contains("phone", StringComparison.OrdinalIgnoreCase) || 
                                    errorMessage.Contains("name", StringComparison.OrdinalIgnoreCase) ||
                                    errorMessage.Contains("text", StringComparison.OrdinalIgnoreCase))
                                {
                                    errorMessage = "Please upload a file.";
                                }
                                
                                modelState.AddModelError(fieldKey, errorMessage);
                                isValid = false;
                            }
                        }
                        else
                        {
                            // For other complex fields (autocomplete), check if value is empty
                            if (string.IsNullOrWhiteSpace(stringValue))
                            {
                                modelState.AddModelError(fieldKey, rule.Message);
                                isValid = false;
                            }
                        }
                        break;
                    case "regex":
                        // Regex validation doesn't apply to upload fields, skip for uploads
                        if (!isUploadField && !string.IsNullOrWhiteSpace(stringValue))
                        {
                            var pattern = rule.Rule?.ToString();
                            if (!string.IsNullOrEmpty(pattern))
                            {
                                var target = ExtractAutocompleteDisplayText(stringValue);
                                if (!Regex.IsMatch(target, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
                                {
                                    modelState.AddModelError(fieldKey, rule.Message);
                                    isValid = false;
                                }
                            }
                        }
                        break;
                    case "maxlength":
                        // MaxLength validation doesn't apply to upload fields, skip for uploads
                        if (!isUploadField)
                        {
                            if (int.TryParse(rule.Rule?.ToString(), out var maxLength))
                            {
                                if (stringValue.Length > maxLength)
                                {
                                    modelState.AddModelError(fieldKey, rule.Message);
                                    isValid = false;
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Complex field maxLength validation rule has invalid rule value for field '{FieldId}': {Rule}", field.FieldId, rule.Rule);
                            }
                        }
                        break;
                    default:
                        _logger.LogWarning("Unknown complex field validation rule type '{Type}' for field '{FieldId}'", rule.Type, field.FieldId);
                        break;
                }
            }

            return isValid;
        }

        /// <summary>
        /// Gets the custom error message from a field's required validation rule, if it exists
        /// </summary>
        /// <param name="field">The field to check</param>
        /// <returns>Custom error message or null</returns>
        private static string? GetCustomRequiredMessage(Field field)
        {
            if (field.Validations != null)
            {
                foreach (var validation in field.Validations)
                {
                    if (string.Equals(validation.Type, "required", StringComparison.OrdinalIgnoreCase))
                    {
                        return validation.Message;
                    }
                }
            }
            
            return null;
        }

        /// <summary>
        /// Checks if an upload field has uploaded files
        /// </summary>
        /// <param name="value">The field value (JSON array or string)</param>
        /// <returns>True if files are uploaded</returns>
        private bool HasUploadedFiles(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            // Handle special session data placeholder - this indicates NO files uploaded yet
            if (value == "UPLOAD_FIELD_SESSION_DATA")
            {
                return false;
            }

            // Try to parse as JSON array
            try
            {
                if (value.StartsWith("[") && value.EndsWith("]"))
                {
                    var files = System.Text.Json.JsonSerializer.Deserialize<List<object>>(value);
                    return files != null && files.Count > 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse upload field value as JSON for field value: {Value}", value);
            }

            // If not JSON or parsing failed, treat non-empty as having files (except for known placeholders)
            return !string.IsNullOrWhiteSpace(value);
        }

        /// <summary>
        /// Attempts to extract a human-readable display text from an autocomplete value.
        /// Values are often JSON objects like { "name": "Trust A", "ukprn": "123" }.
        /// If parsing fails, returns the raw value.
        /// </summary>
        private static string ExtractAutocompleteDisplayText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            try
            {
                using var doc = JsonDocument.Parse(value);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    // Prefer typical display properties
                    var displayProps = new[] { "name", "title", "label", "displayName", "groupName", "text", "value" };
                    foreach (var p in displayProps)
                    {
                        if (doc.RootElement.TryGetProperty(p, out var prop) && prop.ValueKind == JsonValueKind.String)
                        {
                            var s = prop.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) return s!;
                        }
                    }

                    // Otherwise, return a key identifier if present
                    var idProps = new[] { "ukprn", "urn", "id", "companiesHouseNumber", "companieshousenumber", "companies_house_number", "code" };
                    foreach (var p in idProps)
                    {
                        if (doc.RootElement.TryGetProperty(p, out var prop))
                        {
                            if (prop.ValueKind == JsonValueKind.String)
                            {
                                var s = prop.GetString();
                                if (!string.IsNullOrWhiteSpace(s)) return s!;
                            }
                            else if (prop.ValueKind == JsonValueKind.Number)
                            {
                                return prop.GetInt64().ToString(CultureInfo.InvariantCulture);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Not JSON; fall through
            }

            return value;
        }

        #endregion
    }
}
