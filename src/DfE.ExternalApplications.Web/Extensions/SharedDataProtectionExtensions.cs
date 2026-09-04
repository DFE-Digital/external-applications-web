using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DfE.ExternalApplications.Web.Extensions;

/// <summary>
/// Registers a Redis-backed Data Protection key ring so session and auth cookies
/// can be unprotected on any replica after a restart.
/// </summary>
public static class SharedDataProtectionExtensions
{
    /// <summary>
    /// Discriminator shared by all EAT Web instances. Do not change after keys have been issued.
    /// </summary>
    public const string ApplicationName = "DfE.ExternalApplications.Web";

    /// <summary>
    /// Redis key that holds the XML key ring.
    /// </summary>
    public const string RedisKeyName = "DfE:DataProtection-Keys";

    /// <summary>
    /// Persists ASP.NET Data Protection keys to the existing Redis connection.
    /// Cookie payloads stay encrypted; only the key ring is shared.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddSharedDataProtection(this IServiceCollection services)
    {
        services.AddDataProtection()
            .SetApplicationName(ApplicationName);

        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
        {
            var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
            return new ConfigureOptions<KeyManagementOptions>(options =>
            {
                options.XmlRepository = new RedisXmlRepository(
                    () => multiplexer.GetDatabase(),
                    RedisKeyName);
            });
        });

        return services;
    }
}
