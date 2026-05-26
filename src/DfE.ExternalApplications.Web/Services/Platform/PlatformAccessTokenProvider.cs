using Azure.Core;
using Azure.Identity;
using DfE.ExternalApplications.Web.Configuration;
using Microsoft.Extensions.Options;

namespace DfE.ExternalApplications.Web.Services.Platform;

/// <inheritdoc />
public sealed class PlatformAccessTokenProvider(IOptions<PlatformBootstrapOptions> options) : IPlatformAccessTokenProvider
{
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var bootstrap = options.Value;
        if (string.IsNullOrWhiteSpace(bootstrap.Scope))
        {
            throw new InvalidOperationException("PlatformBootstrap:Scope is required when platform bootstrap is enabled.");
        }

        TokenCredential credential = !string.IsNullOrWhiteSpace(bootstrap.ClientSecret)
            ? new ClientSecretCredential(
                bootstrap.TenantId,
                bootstrap.ClientId,
                bootstrap.ClientSecret)
            : new DefaultAzureCredential();

        var token = await credential.GetTokenAsync(
            new TokenRequestContext([bootstrap.Scope]),
            cancellationToken);

        return token.Token;
    }
}
