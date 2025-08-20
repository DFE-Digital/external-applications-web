using DfE.ExternalApplications.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DfE.ExternalApplications.Infrastructure.Services
{
    /// <summary>
    /// Implementation of the form configuration service that provides form settings and configurations
    /// </summary>
    public class FormConfigurationService : IFormConfigurationService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<FormConfigurationService> _logger;

        public FormConfigurationService(IConfiguration configuration, ILogger<FormConfigurationService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Gets the configuration for a specific form template
        /// </summary>
        /// <param name="templateId">The template ID</param>
        /// <returns>The form configuration</returns>
        public FormConfiguration GetFormConfiguration(string templateId)
        {
            var config = new FormConfiguration
            {
                TemplateId = templateId,
                TemplateName = templateId,
                AllowPartialSaving = _configuration.GetValue<bool>("FormEngine:AllowPartialSaving", true),
                RequireAllTasksCompleted = _configuration.GetValue<bool>("FormEngine:RequireAllTasksCompleted", false),
                MaxFileUploadSize = _configuration.GetValue<int>("FormEngine:MaxFileUploadSize", 10 * 1024 * 1024),
                AllowedFileTypes = _configuration.GetSection("FormEngine:AllowedFileTypes").Get<string[]>() ?? new[] { ".pdf", ".doc", ".docx" }
            };

            _logger.LogDebug("Retrieved form configuration for template {TemplateId}", templateId);
            return config;
        }

        /// <summary>
        /// Gets the configuration for a specific field type
        /// </summary>
        /// <param name="fieldType">The field type</param>
        /// <returns>The field configuration</returns>
        public FieldConfiguration GetFieldConfiguration(string fieldType)
        {
            var config = new FieldConfiguration
            {
                FieldType = fieldType,
                IsRequired = _configuration.GetValue<bool>($"FormEngine:FieldTypes:{fieldType}:IsRequired", false),
                MaxLength = _configuration.GetValue<int>($"FormEngine:FieldTypes:{fieldType}:MaxLength", 0),
                DefaultValue = _configuration.GetValue<string>($"FormEngine:FieldTypes:{fieldType}:DefaultValue", string.Empty),
                ValidationRules = _configuration.GetSection($"FormEngine:FieldTypes:{fieldType}:ValidationRules").Get<string[]>() ?? Array.Empty<string>()
            };

            _logger.LogDebug("Retrieved field configuration for type {FieldType}", fieldType);
            return config;
        }

        /// <summary>
        /// Gets the configuration for a specific validation type
        /// </summary>
        /// <param name="validationType">The validation type</param>
        /// <returns>The validation configuration</returns>
        public ValidationConfiguration GetValidationConfiguration(string validationType)
        {
            var config = new ValidationConfiguration
            {
                ValidationType = validationType,
                ErrorMessage = _configuration.GetValue<string>($"FormEngine:ValidationTypes:{validationType}:ErrorMessage", "Validation failed"),
                Rule = _configuration.GetValue<object>($"FormEngine:ValidationTypes:{validationType}:Rule", string.Empty),
                IsConditional = _configuration.GetValue<bool>($"FormEngine:ValidationTypes:{validationType}:IsConditional", false)
            };

            _logger.LogDebug("Retrieved validation configuration for type {ValidationType}", validationType);
            return config;
        }

        /// <summary>
        /// Gets the default form settings
        /// </summary>
        /// <returns>The default form settings</returns>
        public FormSettings GetDefaultFormSettings()
        {
            var settings = new FormSettings
            {
                EnableAutoSave = _configuration.GetValue<bool>("FormEngine:EnableAutoSave", true),
                AutoSaveInterval = _configuration.GetValue<int>("FormEngine:AutoSaveInterval", 30000),
                ShowProgressIndicator = _configuration.GetValue<bool>("FormEngine:ShowProgressIndicator", true),
                EnableFieldValidation = _configuration.GetValue<bool>("FormEngine:EnableFieldValidation", true),
                DefaultDateFormat = _configuration.GetValue<string>("FormEngine:DefaultDateFormat", "dd/MM/yyyy")
            };

            _logger.LogDebug("Retrieved default form settings");
            return settings;
        }
    }
}
