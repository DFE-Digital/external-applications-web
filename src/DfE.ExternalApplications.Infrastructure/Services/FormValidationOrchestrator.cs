using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Task = DfE.ExternalApplications.Domain.Models.Task;

namespace DfE.ExternalApplications.Infrastructure.Services
{
    /// <summary>
    /// Implementation of the form validation orchestrator that handles validation logic
    /// </summary>
    public class FormValidationOrchestrator : IFormValidationOrchestrator
    {
        private readonly ILogger<FormValidationOrchestrator> _logger;

        public FormValidationOrchestrator(ILogger<FormValidationOrchestrator> logger)
        {
            _logger = logger;
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
                return true;
            }

            var isValid = true;
            foreach (var field in page.Fields)
            {
                var key = field.FieldId;
                data.TryGetValue(key, out var rawValue);
                var value = rawValue?.ToString() ?? string.Empty;

                if (!ValidateField(field, value, modelState, key))
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
            if (field?.Validations == null)
            {
                return true;
            }

            var stringValue = value?.ToString() ?? string.Empty;
            var isValid = true;

            foreach (var rule in field.Validations)
            {
                // Conditional application
                if (rule.Condition != null)
                {
                    // This would need access to the full data dictionary to check conditions
                    // For now, we'll skip conditional validation in this context
                    continue;
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
                        if (!Regex.IsMatch(stringValue, rule.Rule.ToString(), RegexOptions.None, TimeSpan.FromMilliseconds(200)) && !string.IsNullOrWhiteSpace(stringValue))
                        {
                            modelState.AddModelError(fieldKey, rule.Message);
                            isValid = false;
                        }
                        break;
                    case "maxLength":
                        if (stringValue.Length > int.Parse(rule.Rule.ToString()))
                        {
                            modelState.AddModelError(fieldKey, rule.Message);
                            isValid = false;
                        }
                        break;
                    default:
                        _logger.LogWarning("Unknown validation rule type: {RuleType}", rule.Type);
                        break;
                }
            }

            return isValid;
        }
    }
}
