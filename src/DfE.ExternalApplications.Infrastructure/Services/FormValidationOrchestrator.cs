using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using System.Text.Json;
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

        public FormValidationOrchestrator(ILogger<FormValidationOrchestrator> logger, IConditionalLogicEngine conditionalLogicEngine)
        {
            _logger = logger;
            _conditionalLogicEngine = conditionalLogicEngine;
        }

        /// <summary>
        /// Validates a single page
        /// </summary>
        /// <param name="page">The page to validate</param>
        /// <param name="data">The form data</param>
        /// <param name="modelState">The model state to add errors to</param>
        /// <returns>True if validation passes</returns>
        public bool ValidatePage(Page page, Dictionary<string, object> data, ModelStateDictionary modelState)
        {
            if (page?.Fields == null)
            {
                _logger.LogDebug("ValidatePage: No fields found on page '{PageId}', validation passed", page?.PageId ?? "null");
                return true;
            }

            _logger.LogDebug("ValidatePage: Validating page '{PageId}' with {FieldCount} fields", page.PageId, page.Fields.Count);

            var isValid = true;
            foreach (var field in page.Fields)
            {
                var key = field.FieldId;
                data.TryGetValue(key, out var rawValue);
                var value = rawValue?.ToString() ?? string.Empty;

                _logger.LogDebug("ValidatePage: Validating field '{FieldId}' with value '{Value}' and {ValidationCount} validation rules", 
                    field.FieldId, value, field.Validations?.Count ?? 0);

                if (!ValidateField(field, value, data, modelState, key))
                {
                    isValid = false;
                }
            }

            _logger.LogDebug("ValidatePage: Page '{PageId}' validation result: {IsValid}", page.PageId, isValid);
            return isValid;
        }

        /// <summary>
        /// Validates a single task
        /// </summary>
        /// <param name="task">The task to validate</param>
        /// <param name="data">The form data</param>
        /// <param name="modelState">The model state to add errors to</param>
        /// <returns>True if validation passes</returns>
        public bool ValidateTask(Task task, Dictionary<string, object> data, ModelStateDictionary modelState)
        {
            if (task?.Pages == null)
            {
                return true;
            }

            var isValid = true;
            foreach (var page in task.Pages)
            {
                if (!ValidatePage(page, data, modelState))
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
            // Call the overloaded method with null data for backward compatibility
            return ValidateField(field, value, null, modelState, fieldKey);
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
            _logger.LogInformation("DEBUG: ValidateField called for field '{FieldId}' with value '{Value}'", field?.FieldId, value?.ToString() ?? "null");

            if (field?.Validations == null)
            {
                _logger.LogInformation("DEBUG: Field '{FieldId}' has no validation rules", field?.FieldId);
                return true;
            }

            _logger.LogInformation("DEBUG: Field '{FieldId}' has {ValidationCount} validation rules", field.FieldId, field.Validations.Count);

            var stringValue = value?.ToString() ?? string.Empty;
            var isValid = true;

            // Special handling for complex fields (upload, autocomplete, etc.)
            if (field.Type == "complexField" && field.ComplexField != null)
            {
                _logger.LogInformation("DEBUG: Processing complex field '{FieldId}' with ComplexField.Id '{ComplexFieldId}'", field.FieldId, field.ComplexField.Id);
                return ValidateComplexField(field, value, formData, modelState, fieldKey);
            }

            foreach (var rule in field.Validations)
            {
                _logger.LogInformation("DEBUG: Processing validation rule - Type: '{Type}', Rule: '{Rule}', Message: '{Message}' for field '{FieldId}'", 
                    rule.Type, rule.Rule, rule.Message, field.FieldId);

                // Check if this is a conditional validation rule
                if (rule.Condition != null)
                {
                    _logger.LogInformation("DEBUG: Rule has condition, evaluating...");
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
                            _logger.LogInformation("DEBUG: Conditional validation rule for field '{FieldId}' skipped - condition not met", field.FieldId);
                            continue;
                        }
                        
                        _logger.LogInformation("DEBUG: Conditional validation rule for field '{FieldId}' applied - condition met", field.FieldId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error evaluating conditional validation rule for field '{FieldId}'. Skipping rule.", field.FieldId);
                    continue;
                    }
                }

                // Apply the validation rule
                _logger.LogInformation("DEBUG: Applying validation rule '{Type}' to field '{FieldId}' with value '{Value}'", rule.Type, field.FieldId, stringValue);

                switch (rule.Type)
                {
                    case "required":
                        if (string.IsNullOrWhiteSpace(stringValue))
                        {
                            _logger.LogInformation("DEBUG: Required validation FAILED for field '{FieldKey}': value is empty/whitespace", fieldKey);
                            modelState.AddModelError(fieldKey, rule.Message);
                            isValid = false;
                        }
                        else
                        {
                            _logger.LogInformation("DEBUG: Required validation PASSED for field '{FieldKey}': value is not empty", fieldKey);
                        }
                        break;
                    case "regex":
                        var pattern = rule.Rule?.ToString();
                        if (!string.IsNullOrWhiteSpace(stringValue) && !string.IsNullOrEmpty(pattern))
                        {
                            var regexMatch = Regex.IsMatch(stringValue, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200));
                            if (!regexMatch)
                            {
                                _logger.LogInformation("DEBUG: Regex validation FAILED for field '{FieldKey}': value '{Value}' does not match pattern '{Pattern}'", fieldKey, stringValue, pattern);
                            modelState.AddModelError(fieldKey, rule.Message);
                            isValid = false;
                            }
                            else
                            {
                                _logger.LogInformation("DEBUG: Regex validation PASSED for field '{FieldKey}': value '{Value}' matches pattern '{Pattern}'", fieldKey, stringValue, pattern);
                            }
                        }
                        else
                        {
                            _logger.LogInformation("DEBUG: Regex validation SKIPPED for field '{FieldKey}': value is empty or pattern is null", fieldKey);
                        }
                        break;
                    case "maxLength":
                        var maxLengthStr = rule.Rule?.ToString();
                        if (!string.IsNullOrEmpty(maxLengthStr) && int.TryParse(maxLengthStr, out var maxLength))
                        {
                            if (stringValue.Length > maxLength)
                            {
                                _logger.LogInformation("DEBUG: MaxLength validation FAILED for field '{FieldKey}': length {ActualLength} exceeds maximum {MaxLength}", fieldKey, stringValue.Length, maxLength);
                                modelState.AddModelError(fieldKey, rule.Message);
                                isValid = false;
                            }
                            else
                            {
                                _logger.LogInformation("DEBUG: MaxLength validation PASSED for field '{FieldKey}': length {ActualLength} is within limit {MaxLength}", fieldKey, stringValue.Length, maxLength);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("DEBUG: MaxLength validation SKIPPED for field '{FieldKey}': could not parse max length '{MaxLengthStr}'", fieldKey, maxLengthStr);
                        }
                        break;
                    default:
                        _logger.LogWarning("Unknown validation rule type: {RuleType} for field '{FieldKey}'", rule.Type, fieldKey);
                        break;
                }
            }

            _logger.LogInformation("DEBUG: ValidateField completed for field '{FieldId}': isValid = {IsValid}", field.FieldId, isValid);
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
        /// <returns>True if validation passes</returns>
        private bool ValidateComplexField(Field field, object? value, Dictionary<string, object>? formData, ModelStateDictionary modelState, string fieldKey)
        {
            if (field.Validations == null)
            {
                return true;
            }

            var stringValue = value?.ToString() ?? string.Empty;
            var isValid = true;
            
            // Determine if this is an upload field
            bool isUploadField = field.ComplexField!.Id.Contains("Upload", StringComparison.OrdinalIgnoreCase);
            
            _logger.LogInformation("DEBUG: Complex field '{FieldId}' is upload field: {IsUploadField}", field.FieldId, isUploadField);

            foreach (var rule in field.Validations)
            {
                _logger.LogInformation("DEBUG: Processing complex field validation rule - Type: '{Type}', Rule: '{Rule}', Message: '{Message}' for field '{FieldId}'", 
                    rule.Type, rule.Rule, rule.Message, field.FieldId);

                // Check if this is a conditional validation rule
                if (rule.Condition != null)
                {
                    _logger.LogInformation("DEBUG: Complex field rule has condition, evaluating...");
                    if (formData == null)
                    {
                        _logger.LogWarning("Conditional validation rule found for complex field '{FieldId}' but no form data provided for evaluation. Skipping rule.", field.FieldId);
                        continue;
                    }

                    try
                    {
                        var conditionResult = _conditionalLogicEngine.EvaluateCondition(rule.Condition, formData);
                        _logger.LogInformation("DEBUG: Complex field conditional validation result for field '{FieldId}': {ConditionResult}", field.FieldId, conditionResult);
                        
                        if (!conditionResult)
                        {
                            _logger.LogInformation("DEBUG: Complex field conditional validation rule skipped for field '{FieldId}' - condition not met", field.FieldId);
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error evaluating conditional validation rule for complex field '{FieldId}'. Skipping rule.", field.FieldId);
                        continue;
                    }
                }

                _logger.LogInformation("DEBUG: Applying complex field validation rule '{Type}' to field '{FieldId}' with value '{Value}'", rule.Type, field.FieldId, stringValue);

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
                                    _logger.LogInformation("DEBUG: Replaced inappropriate upload field error message with default: '{ErrorMessage}'", errorMessage);
                                }
                                
                                modelState.AddModelError(fieldKey, errorMessage);
                                isValid = false;
                                _logger.LogInformation("DEBUG: Upload field required validation FAILED for field '{FieldId}': no files uploaded", field.FieldId);
                            }
                            else
                            {
                                _logger.LogInformation("DEBUG: Upload field required validation PASSED for field '{FieldId}': files are uploaded", field.FieldId);
                            }
                        }
                        else
                        {
                            // For other complex fields (autocomplete), check if value is empty
                            if (string.IsNullOrWhiteSpace(stringValue))
                            {
                                modelState.AddModelError(fieldKey, rule.Message);
                                isValid = false;
                                _logger.LogInformation("DEBUG: Complex field required validation FAILED for field '{FieldId}': value is empty/whitespace", field.FieldId);
                            }
                            else
                            {
                                _logger.LogInformation("DEBUG: Complex field required validation PASSED for field '{FieldId}': value is not empty", field.FieldId);
                            }
                        }
                        break;
                    case "regex":
                        // Regex validation doesn't apply to upload fields, skip for uploads
                        if (!isUploadField && !string.IsNullOrWhiteSpace(stringValue))
                        {
                            var pattern = rule.Rule?.ToString();
                            if (!string.IsNullOrEmpty(pattern) && !Regex.IsMatch(stringValue, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
                            {
                                modelState.AddModelError(fieldKey, rule.Message);
                                isValid = false;
                                _logger.LogInformation("DEBUG: Complex field regex validation FAILED for field '{FieldId}': value '{Value}' does not match pattern '{Pattern}'", field.FieldId, stringValue, pattern);
                            }
                            else
                            {
                                _logger.LogInformation("DEBUG: Complex field regex validation PASSED for field '{FieldId}': value '{Value}' matches pattern '{Pattern}'", field.FieldId, stringValue, pattern);
                            }
                        }
                        else
                        {
                            _logger.LogInformation("DEBUG: Complex field regex validation SKIPPED for field '{FieldId}': upload field or empty value", field.FieldId);
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
                                    _logger.LogInformation("DEBUG: Complex field maxLength validation FAILED for field '{FieldId}': length {Length} exceeds limit {MaxLength}", field.FieldId, stringValue.Length, maxLength);
                                }
                                else
                                {
                                    _logger.LogInformation("DEBUG: Complex field maxLength validation PASSED for field '{FieldId}': length {Length} is within limit {MaxLength}", field.FieldId, stringValue.Length, maxLength);
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Complex field maxLength validation rule has invalid rule value for field '{FieldId}': {Rule}", field.FieldId, rule.Rule);
                            }
                        }
                        else
                        {
                            _logger.LogInformation("DEBUG: Complex field maxLength validation SKIPPED for field '{FieldId}': upload field", field.FieldId);
                        }
                        break;
                    default:
                        _logger.LogWarning("Unknown complex field validation rule type '{Type}' for field '{FieldId}'", rule.Type, field.FieldId);
                        break;
                }
            }

            _logger.LogInformation("DEBUG: Complex field ValidateField completed for field '{FieldId}': isValid = {IsValid}", field.FieldId, isValid);
            return isValid;
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
                _logger.LogInformation("DEBUG: Upload field value is null/empty - no files uploaded");
                return false;
            }

            // Handle special session data placeholder - this indicates NO files uploaded yet
            if (value == "UPLOAD_FIELD_SESSION_DATA")
            {
                _logger.LogInformation("DEBUG: Upload field contains session data placeholder - no files uploaded yet");
                return false;
            }

            // Try to parse as JSON array
            try
            {
                if (value.StartsWith("[") && value.EndsWith("]"))
                {
                    var files = System.Text.Json.JsonSerializer.Deserialize<List<object>>(value);
                    bool hasFiles = files != null && files.Count > 0;
                    _logger.LogInformation("DEBUG: Upload field JSON array contains {FileCount} files", files?.Count ?? 0);
                    return hasFiles;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse upload field value as JSON for field value: {Value}", value);
            }

            // If not JSON or parsing failed, treat non-empty as having files (except for known placeholders)
            _logger.LogInformation("DEBUG: Upload field value is non-JSON, treating as {HasFiles}", !string.IsNullOrWhiteSpace(value));
            return !string.IsNullOrWhiteSpace(value);
        }

        #endregion
    }
}
