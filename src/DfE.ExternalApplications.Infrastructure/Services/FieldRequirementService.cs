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
                    return true;
                }
            }
        }

        // Priority 2: Check field's Required property
        if (field.Required.HasValue)
        {
            return field.Required.Value;
        }

        // Priority 3: Use template's default policy
        var defaultPolicy = template.DefaultFieldRequirementPolicy;
        
        if (string.IsNullOrWhiteSpace(defaultPolicy))
        {
            // Backward compatibility: if no policy is set, default to optional
            return false;
        }

        return string.Equals(defaultPolicy, PolicyRequired, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets all required fields for a task
    /// </summary>
    public List<string> GetRequiredFieldsForTask(Task task, FormTemplate template, Func<string, bool>? isFieldHidden = null)
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
                // Check if field is hidden by conditional logic
                var isHidden = isFieldHidden != null && isFieldHidden(field.FieldId);
                
                // Skip fields that are hidden by conditional logic
                if (isHidden)
                {
                    continue;
                }
                
                var isRequired = IsFieldRequired(field, template);
                
                if (isRequired)
                {
                    requiredFields.Add(field.FieldId);
                }
            }
        }

        return requiredFields;
    }

    /// <summary>
    /// Validates that all required fields in a task have values
    /// </summary>
    public List<string> GetMissingRequiredFields(Task task, FormTemplate template, Dictionary<string, object> formData, Func<string, bool>? isFieldHidden = null)
    {
        var missingFields = new List<string>();
        
        var requiredFields = GetRequiredFieldsForTask(task, template, isFieldHidden);

        foreach (var fieldId in requiredFields)
        {
            if (!formData.TryGetValue(fieldId, out var value) || IsFieldValueEmpty(value))
            {
                missingFields.Add(fieldId);
            }
        }
        
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

