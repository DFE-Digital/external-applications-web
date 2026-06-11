using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DfE.ExternalApplications.Infrastructure.Services
{
    public class ComplexFieldConfigurationService : IComplexFieldConfigurationService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ComplexFieldConfigurationService> _logger;

        public ComplexFieldConfigurationService(IConfiguration configuration, ILogger<ComplexFieldConfigurationService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public ComplexFieldConfiguration GetConfiguration(string complexFieldId)
        {
            // First try the new structure (array of objects with Id property)
            var complexFieldsSection = _configuration.GetSection("FormEngine:ComplexFields");
            if (complexFieldsSection.Exists())
            {
                var configurations = complexFieldsSection.Get<List<ComplexFieldConfiguration>>();
                if (configurations != null)
                {
                    var config = configurations.FirstOrDefault(c => c.Id == complexFieldId);
                    if (config != null)
                    {
                        ApplySharedApiKeyFallback(config, configurations);
                        _logger.LogDebug(
                            "Loaded complex field configuration for {ComplexFieldId}: Endpoint={Endpoint}, AllowMultiple={AllowMultiple}, MinLength={MinLength}, HasApiKey={HasApiKey}",
                            complexFieldId, config.ApiEndpoint, config.AllowMultiple, config.MinLength, !string.IsNullOrEmpty(config.ApiKey));
                        return config;
                    }
                }
            }

            // Fallback to old structure (direct key lookup)
            var configSection = _configuration.GetSection($"FormEngine:ComplexFields:{complexFieldId}");
            
            if (!configSection.Exists())
            {
                _logger.LogWarning("Complex field configuration not found for ID: {ComplexFieldId}", complexFieldId);
                return new ComplexFieldConfiguration { Id = complexFieldId };
            }

            var configuration = new ComplexFieldConfiguration
            {
                Id = complexFieldId,
                ApiEndpoint = configSection["ApiEndpoint"] ?? string.Empty,
                ApiKey = configSection["ApiKey"] ?? string.Empty,
                FieldType = configSection["FieldType"] ?? "autocomplete",
                AllowMultiple = bool.TryParse(configSection["AllowMultiple"], out var allowMultiple) ? allowMultiple : false,
                MinLength = int.TryParse(configSection["MinLength"], out var minLength) ? minLength : 3,
                Placeholder = configSection["Placeholder"] ?? "Start typing to search...",
                MaxSelections = int.TryParse(configSection["MaxSelections"], out var maxSelections) ? maxSelections : 0,
                Label = configSection["Label"] ?? "Item"
            };

            // Load additional properties from configuration
            foreach (var child in configSection.GetChildren())
            {
                if (!new[] { "ApiEndpoint", "ApiKey", "FieldType", "AllowMultiple", "MinLength", "Placeholder", "MaxSelections", "Label" }.Contains(child.Key))
                {
                    configuration.AdditionalProperties[child.Key] = child.Value ?? "";
                }
            }

            if (string.IsNullOrEmpty(configuration.ApiKey))
            {
                var allConfigurations = complexFieldsSection.Exists()
                    ? complexFieldsSection.Get<List<ComplexFieldConfiguration>>()
                    : null;
                if (allConfigurations != null)
                {
                    ApplySharedApiKeyFallback(configuration, allConfigurations);
                }
            }

            _logger.LogDebug(
                "Loaded complex field configuration for {ComplexFieldId}: Endpoint={Endpoint}, AllowMultiple={AllowMultiple}, MinLength={MinLength}, HasApiKey={HasApiKey}",
                complexFieldId, configuration.ApiEndpoint, configuration.AllowMultiple, configuration.MinLength, !string.IsNullOrEmpty(configuration.ApiKey));

            return configuration;
        }

        /// <summary>
        /// Reuses the Academies API key from another complex field when this field has none configured.
        /// New fields (e.g. LocalAuthorityComplexField, DioceseComplexField) are often added without a dedicated user-secret entry.
        /// </summary>
        private void ApplySharedApiKeyFallback(ComplexFieldConfiguration config, List<ComplexFieldConfiguration> allConfigurations)
        {
            if (!string.IsNullOrEmpty(config.ApiKey))
            {
                return;
            }

            config.ApiKey = allConfigurations
                .Where(c => c.Id != config.Id && !string.IsNullOrEmpty(c.ApiKey))
                .Select(c => c.ApiKey)
                .FirstOrDefault()
                ?? _configuration["FormEngine:AcademiesApiKey"]
                ?? string.Empty;
        }

        public bool HasConfiguration(string complexFieldId)
        {
            // First try the new structure (array of objects with Id property)
            var complexFieldsSection = _configuration.GetSection("FormEngine:ComplexFields");
            if (complexFieldsSection.Exists())
            {
                var configurations = complexFieldsSection.Get<List<ComplexFieldConfiguration>>();
                if (configurations != null)
                {
                    return configurations.Any(c => c.Id == complexFieldId);
                }
            }

            // Fallback to old structure (direct key lookup)
            var configSection = _configuration.GetSection($"FormEngine:ComplexFields:{complexFieldId}");
            return configSection.Exists();
        }
    }
} 