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
                        _logger.LogDebug("Loaded complex field configuration for {ComplexFieldId}: Endpoint={Endpoint}, AllowMultiple={AllowMultiple}, MinLength={MinLength}", 
                            complexFieldId, config.ApiEndpoint, config.AllowMultiple, config.MinLength);
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

            _logger.LogDebug("Loaded complex field configuration for {ComplexFieldId}: Endpoint={Endpoint}, AllowMultiple={AllowMultiple}, MinLength={MinLength}", 
                complexFieldId, configuration.ApiEndpoint, configuration.AllowMultiple, configuration.MinLength);

            return configuration;
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