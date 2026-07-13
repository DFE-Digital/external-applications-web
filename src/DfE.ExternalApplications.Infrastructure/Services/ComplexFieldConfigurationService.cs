using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DfE.ExternalApplications.Infrastructure.Services;

/// <summary>
/// Loads complex field configuration from the effective request configuration.
/// </summary>
public sealed class ComplexFieldConfigurationService(
    IRequestAppConfiguration requestConfiguration,
    ILogger<ComplexFieldConfigurationService> logger) : IComplexFieldConfigurationService
{
    /// <inheritdoc />
    public ComplexFieldConfiguration GetConfiguration(string complexFieldId)
    {
        var configuration = requestConfiguration.Current;

        // First try the new structure (array of objects with Id property)
        var complexFieldsSection = configuration.GetSection("FormEngine:ComplexFields");
        if (complexFieldsSection.Exists())
        {
            var configurations = complexFieldsSection.Get<List<ComplexFieldConfiguration>>();
            if (configurations != null)
            {
                var config = configurations.FirstOrDefault(c => c.Id == complexFieldId);
                if (config != null)
                {
                    ApplySharedApiKeyFallback(config, configurations, configuration);
                    logger.LogDebug(
                        "Loaded complex field configuration for {ComplexFieldId}: Endpoint={Endpoint}, AllowMultiple={AllowMultiple}, MinLength={MinLength}, HasApiKey={HasApiKey}",
                        complexFieldId, config.ApiEndpoint, config.AllowMultiple, config.MinLength, !string.IsNullOrEmpty(config.ApiKey));
                    return config;
                }
            }
        }

        // Fallback to old structure (direct key lookup)
        var configSection = configuration.GetSection($"FormEngine:ComplexFields:{complexFieldId}");

        if (!configSection.Exists())
        {
            logger.LogWarning("Complex field configuration not found for ID: {ComplexFieldId}", complexFieldId);
            return new ComplexFieldConfiguration { Id = complexFieldId };
        }

        var fieldConfiguration = new ComplexFieldConfiguration
        {
            Id = complexFieldId,
            ApiEndpoint = configSection["ApiEndpoint"] ?? string.Empty,
            ApiKey = configSection["ApiKey"] ?? string.Empty,
            FieldType = configSection["FieldType"] ?? "autocomplete",
            AllowMultiple = bool.TryParse(configSection["AllowMultiple"], out var allowMultiple) && allowMultiple,
            MinLength = int.TryParse(configSection["MinLength"], out var minLength) ? minLength : 3,
            Placeholder = configSection["Placeholder"] ?? "Start typing to search...",
            MaxSelections = int.TryParse(configSection["MaxSelections"], out var maxSelections) ? maxSelections : 0,
            Label = configSection["Label"] ?? "Item"
        };

        foreach (var child in configSection.GetChildren())
        {
            if (!new[] { "ApiEndpoint", "ApiKey", "FieldType", "AllowMultiple", "MinLength", "Placeholder", "MaxSelections", "Label" }.Contains(child.Key))
            {
                fieldConfiguration.AdditionalProperties[child.Key] = child.Value ?? "";
            }
        }

        if (string.IsNullOrEmpty(fieldConfiguration.ApiKey))
        {
            var allConfigurations = complexFieldsSection.Exists()
                ? complexFieldsSection.Get<List<ComplexFieldConfiguration>>()
                : null;
            if (allConfigurations != null)
            {
                ApplySharedApiKeyFallback(fieldConfiguration, allConfigurations, configuration);
            }
        }

        logger.LogDebug(
            "Loaded complex field configuration for {ComplexFieldId}: Endpoint={Endpoint}, AllowMultiple={AllowMultiple}, MinLength={MinLength}, HasApiKey={HasApiKey}",
            complexFieldId, fieldConfiguration.ApiEndpoint, fieldConfiguration.AllowMultiple, fieldConfiguration.MinLength, !string.IsNullOrEmpty(fieldConfiguration.ApiKey));

        return fieldConfiguration;
    }

    /// <summary>
    /// Reuses the Academies API key from another complex field when this field has none configured.
    /// </summary>
    private static void ApplySharedApiKeyFallback(
        ComplexFieldConfiguration config,
        List<ComplexFieldConfiguration> allConfigurations,
        IConfiguration configuration)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            return;
        }

        config.ApiKey = allConfigurations
            .Where(c => c.Id != config.Id && !string.IsNullOrEmpty(c.ApiKey))
            .Select(c => c.ApiKey)
            .FirstOrDefault()
            ?? configuration["FormEngine:AcademiesApiKey"]
            ?? string.Empty;
    }

    /// <inheritdoc />
    public bool HasConfiguration(string complexFieldId)
    {
        var configuration = requestConfiguration.Current;
        var complexFieldsSection = configuration.GetSection("FormEngine:ComplexFields");
        if (complexFieldsSection.Exists())
        {
            var configurations = complexFieldsSection.Get<List<ComplexFieldConfiguration>>();
            if (configurations != null)
            {
                return configurations.Any(c => c.Id == complexFieldId);
            }
        }

        var configSection = configuration.GetSection($"FormEngine:ComplexFields:{complexFieldId}");
        return configSection.Exists();
    }
}
