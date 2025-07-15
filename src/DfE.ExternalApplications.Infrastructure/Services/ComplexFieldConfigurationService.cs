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
            var configSection = _configuration.GetSection($"FormEngine:ComplexFields:{complexFieldId}");
            
            if (!configSection.Exists())
            {
                _logger.LogWarning("Complex field configuration not found for ID: {ComplexFieldId}", complexFieldId);
                return new ComplexFieldConfiguration();
            }

            var configuration = new ComplexFieldConfiguration
            {
                ApiEndpoint = configSection["ApiEndpoint"] ?? string.Empty,
                ApiKey = configSection["ApiKey"] ?? string.Empty,
                AllowMultiple = bool.TryParse(configSection["AllowMultiple"], out var allowMultiple) ? allowMultiple : false,
                MinLength = int.TryParse(configSection["MinLength"], out var minLength) ? minLength : 3,
                Placeholder = configSection["Placeholder"] ?? "Start typing to search...",
                MaxSelections = int.TryParse(configSection["MaxSelections"], out var maxSelections) ? maxSelections : 0,
                Label = configSection["Label"] ?? "Item"
            };

            _logger.LogDebug("Loaded complex field configuration for {ComplexFieldId}: Endpoint={Endpoint}, AllowMultiple={AllowMultiple}, MinLength={MinLength}", 
                complexFieldId, configuration.ApiEndpoint, configuration.AllowMultiple, configuration.MinLength);

            return configuration;
        }

        public bool HasConfiguration(string complexFieldId)
        {
            var configSection = _configuration.GetSection($"FormEngine:ComplexFields:{complexFieldId}");
            return configSection.Exists();
        }
    }
} 