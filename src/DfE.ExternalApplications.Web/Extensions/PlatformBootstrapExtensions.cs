using DfE.ExternalApplications.Web.Configuration;
using DfE.ExternalApplications.Web.Middleware;
using DfE.ExternalApplications.Web.Services.Platform;
using DfE.ExternalApplications.Web.Services.Tenant;
using DfE.ExternalApplications.Web.Tenancy;

namespace DfE.ExternalApplications.Web.Extensions;

/// <summary>
/// Registers platform bootstrap and per-request tenant configuration services.
/// </summary>
public static class PlatformBootstrapExtensions
{
    public static IServiceCollection AddPlatformTenantConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PlatformBootstrapOptions>(configuration.GetSection(PlatformBootstrapOptions.SectionName));

        var bootstrap = configuration.GetSection(PlatformBootstrapOptions.SectionName).Get<PlatformBootstrapOptions>();
        if (bootstrap is not { Enabled: true })
        {
            services.AddScoped<ITenantRequestContext, TenantRequestContext>();
            return services;
        }

        services.AddHttpClient<PlatformConfigurationApiClient>();
        services.AddSingleton<IPlatformAccessTokenProvider, PlatformAccessTokenProvider>();
        services.AddSingleton<ITenantConfigurationCache, TenantConfigurationCache>();
        services.AddScoped<TenantConfigurationLoader>();
        services.AddScoped<ITenantIdResolver, TenantIdResolver>();
        services.AddScoped<PlatformHostConfigurationBootstrapper>();
        services.AddScoped<ITenantRequestContext, TenantRequestContext>();
        services.AddTransient<TenantApiRequestHandler>();

        services.AddHttpClient().ConfigureHttpClientDefaults(http =>
            http.AddHttpMessageHandler<TenantApiRequestHandler>());

        return services;
    }

    /// <summary>
    /// Loads host configuration from the platform API and merges it into the application configuration.
    /// Call before <see cref="WebApplicationBuilder.Build"/>.
    /// </summary>
    public static async Task BootstrapPlatformHostConfigurationAsync(this WebApplicationBuilder builder)
    {
        var bootstrap = builder.Configuration
            .GetSection(PlatformBootstrapOptions.SectionName)
            .Get<PlatformBootstrapOptions>();

        if (bootstrap is not { Enabled: true })
        {
            return;
        }

        using var scope = builder.Services.BuildServiceProvider().CreateScope();
        var hostBootstrapper = scope.ServiceProvider.GetRequiredService<PlatformHostConfigurationBootstrapper>();
        var hostConfiguration = await hostBootstrapper.LoadHostConfigurationAsync();

        builder.Configuration.AddInMemoryCollection(hostConfiguration);
    }

    public static IApplicationBuilder UsePlatformTenantConfiguration(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TenantConfigurationMiddleware>();
    }
}
