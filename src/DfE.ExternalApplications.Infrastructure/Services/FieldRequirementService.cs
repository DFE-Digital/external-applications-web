using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using Microsoft.Extensions.Logging;
using Task = DfE.ExternalApplications.Domain.Models.Task;

namespace DfE.ExternalApplications.Infrastructure.Services;

/// <summary>
/// Service to determine if a field is required based on template policy and field configuration
/// </summary>
public class FieldRequirementService : IFieldRequirementService
{
    private readonly ILogger<FieldRequirementService> _logger;
    private const string PolicyRequired = "required";
    private const string PolicyOptional = "optional";

    public FieldRequirementService(ILogger<FieldRequirementService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Determines if a field is required based on the template's default policy,
    /// field's Required property, and field's validation rules
    /// </summary>
    public bool IsFieldRequired(Field field, FormTemplate template)
    {
        // Priority 1: Check if field has explicit validation rule with type="required"
        if (field.Validations != null)
        {
            foreach (var validation in field.Validations)
            {
                if (string.Equals(validation.Type, "required", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Field {FieldId} is required due to validation rule", field.FieldId);
                    return true;
                }
            }
        }

        // Priority 2: Check field's Required property
        if (field.Required.HasValue)
        {
            _logger.LogDebug("Field {FieldId} required status explicitly set to {Required}", field.FieldId, field.Required.Value);
            return field.Required.Value;
        }

        // Priority 3: Use template's default policy
        var defaultPolicy = template.DefaultFieldRequirementPolicy;
        
        if (string.IsNullOrWhiteSpace(defaultPolicy))
        {
            // Backward compatibility: if no policy is set, default to optional
            _logger.LogDebug("Field {FieldId} using default 'optional' policy (no template policy set)", field.FieldId);
            return false;
        }

        var isRequiredByPolicy = string.Equals(defaultPolicy, PolicyRequired, StringComparison.OrdinalIgnoreCase);
        _logger.LogDebug("Field {FieldId} required status based on template policy '{Policy}': {Required}", 
            field.FieldId, defaultPolicy, isRequiredByPolicy);
        
        return isRequiredByPolicy;
    }

    /// <summary>
    /// Gets all required fields for a task
    /// </summary>
    public List<string> GetRequiredFieldsForTask(Task task, FormTemplate template)
    {
        var requiredFields = new List<string>();

        if (task?.Pages == null)
        {
            return requiredFields;
        }

        foreach (var page in task.Pages)
        {
            if (page?.Fields == null) continue;

            foreach (var field in page.Fields)
            {
                var isRequired = IsFieldRequired(field, template);
                _logger.LogInformation("Field {FieldId}: IsRequired = {IsRequired}, Field.Required = {FieldRequired}, HasValidationRules = {HasRules}", 
                    field.FieldId, isRequired, field.Required?.ToString() ?? "null", field.Validations?.Any() ?? false);
                
                if (isRequired)
                {
                    requiredFields.Add(field.FieldId);
                }
            }
        }

        _logger.LogInformation("Task {TaskId} has {Count} required fields: {Fields}", 
            task.TaskId, requiredFields.Count, string.Join(", ", requiredFields));
        return requiredFields;
    }

    /// <summary>
    /// Validates that all required fields in a task have values
    /// </summary>
    public List<string> GetMissingRequiredFields(Task task, FormTemplate template, Dictionary<string, object> formData)
    {
        var missingFields = new List<string>();
        
        _logger.LogInformation("GetMissingRequiredFields called for task {TaskId}. Template policy: {Policy}", 
            task.TaskId, template?.DefaultFieldRequirementPolicy ?? "null");
        
        var requiredFields = GetRequiredFieldsForTask(task, template);
        
        _logger.LogInformation("Found {Count} required fields in task {TaskId}", requiredFields.Count, task.TaskId);

        foreach (var fieldId in requiredFields)
        {
            if (!formData.TryGetValue(fieldId, out var value) || IsFieldValueEmpty(value))
            {
                missingFields.Add(fieldId);
                _logger.LogInformation("Required field {FieldId} is missing or empty", fieldId);
            }
            else
            {
                _logger.LogDebug("Required field {FieldId} has value: {Value}", fieldId, value);
            }
        }

        _logger.LogInformation("Task {TaskId} has {MissingCount} out of {TotalRequired} required fields missing", 
            task.TaskId, missingFields.Count, requiredFields.Count);
        
        return missingFields;
    }

    /// <summary>
    /// Checks if a field value is considered empty
    /// </summary>
    private static bool IsFieldValueEmpty(object? value)
    {
        if (value == null) return true;
        
        var stringValue = value.ToString();
        return string.IsNullOrWhiteSpace(stringValue);
    }
}

