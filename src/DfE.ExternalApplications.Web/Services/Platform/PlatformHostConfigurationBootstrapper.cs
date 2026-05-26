using DfE.ExternalApplications.Web.Configuration;

namespace DfE.ExternalApplications.Web.Services.Platform;

/// <summary>
/// Loads global host configuration from the platform API at Web startup.
/// </summary>
public sealed class PlatformHostConfigurationBootstrapper(
    PlatformConfigurationApiClient apiClient,
    ILogger<PlatformHostConfigurationBootstrapper> logger)
{
    public async Task<IReadOnlyDictionary<string, string?>> LoadHostConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await apiClient.GetHostConfigurationAsync("Web", cancellationToken);
        logger.LogInformation(
            "Loaded platform host configuration at {LoadedAtUtc} with {KeyCount} keys",
            response.LoadedAtUtc,
            response.Configuration.Count);

        return response.Configuration;
    }
}
