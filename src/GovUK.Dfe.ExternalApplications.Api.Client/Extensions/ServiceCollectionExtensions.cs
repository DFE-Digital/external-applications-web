using System;
using System.Linq;
using System.Net.Http;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;
using GovUK.Dfe.ExternalApplications.Api.Client.Settings;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Extensions
{
    [ExcludeFromCodeCoverage]
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddExternalApplicationsApiClient<TClientInterface, TClientImplementation>(
            this IServiceCollection services,
            IConfiguration configuration,
            HttpClient? existingHttpClient = null)
            where TClientInterface : class
            where TClientImplementation : class, TClientInterface
        {
            var apiSettings = new ApiClientSettings();
            configuration.GetSection("ExternalApplicationsApiClient").Bind(apiSettings);

            services.AddSingleton(apiSettings);
            services.AddSingleton<ITokenAcquisitionService, TokenAcquisitionService>();
            services.AddHttpContextAccessor();
            
            // Register handlers
            services.AddTransient<HeaderForwardingHandler>();
            services.AddTransient<AzureBearerTokenHandler>();
            
            if (apiSettings.RequestTokenExchange)
            {
                // Frontend clients need internal token storage and exchange handler
                services.AddScoped<IInternalUserTokenStore, CachedInternalUserTokenStore>();
                
                services.AddScoped<ICacheManager, DistributedCacheManager>();
                services.AddScoped<ITokenStateManager, TokenStateManager>();

                if (!services.Any(x => x.ServiceType == typeof(IDistributedCache)))
                {
                    services.AddMemoryCache();
                    services.AddDistributedMemoryCache();
                }
                
                services.AddTransient<TokenExchangeHandler>(serviceProvider =>
                {
                    return new TokenExchangeHandler(
                        serviceProvider.GetRequiredService<IHttpContextAccessor>(),
                        serviceProvider.GetRequiredService<IInternalUserTokenStore>(),
                        serviceProvider.GetRequiredService<ITokensClient>(),
                        serviceProvider.GetRequiredService<ITokenAcquisitionService>(),
                        serviceProvider.GetRequiredService<ITokenStateManager>(),
                        serviceProvider.GetRequiredService<ILogger<TokenExchangeHandler>>());
                });

                using (var tempProvider = services.BuildServiceProvider())
                {
                    var logger = tempProvider.GetService<ILoggerFactory>()?.CreateLogger("ExternalApplicationsApiClient");

                    if (apiSettings.AutoRegisterUsers)
                    {
                        logger?.LogInformation("Auto user registration is enabled for ExternalApplicationsApiClient (BaseUrl: {BaseUrl})", apiSettings.BaseUrl);
                    }
                    else
                    {
                        logger?.LogInformation("Auto user registration is disabled for ExternalApplicationsApiClient (BaseUrl: {BaseUrl})", apiSettings.BaseUrl);
                    }
                }

                // Register UserAutoRegistrationHandler for auto-registering new users
                if (apiSettings.AutoRegisterUsers)
                {
                    // Simple registration; the handler manually uses IHttpClientFactory and sets Azure token
                    services.AddTransient<UserAutoRegistrationHandler>(serviceProvider =>
                    {
                        return new UserAutoRegistrationHandler(
                            serviceProvider.GetRequiredService<IHttpClientFactory>(),
                            serviceProvider.GetRequiredService<ITokenStateManager>(),
                            serviceProvider.GetRequiredService<ITokenAcquisitionService>(),
                            serviceProvider.GetRequiredService<IHttpContextAccessor>(),
                            apiSettings,
                            serviceProvider.GetRequiredService<ILogger<UserAutoRegistrationHandler>>());
                    });
                }
            }

            if (existingHttpClient != null)
            {
                services.AddSingleton(existingHttpClient);
                services.AddTransient<TClientInterface, TClientImplementation>(serviceProvider =>
                {
                    return ActivatorUtilities.CreateInstance<TClientImplementation>(
                        serviceProvider, existingHttpClient, apiSettings.BaseUrl!);
                });
            }
            else
            {
                var builder = services.AddHttpClient<TClientInterface, TClientImplementation>((httpClient, serviceProvider) =>
                {
                    httpClient.BaseAddress = new Uri(apiSettings.BaseUrl!);

                    return ActivatorUtilities.CreateInstance<TClientImplementation>(
                        serviceProvider, httpClient, apiSettings.BaseUrl!);
                });

                // Add header forwarding handler FIRST in the chain
                // This ensures headers like X-Cypress-Test are available for all subsequent handlers
                builder.AddHttpMessageHandler<HeaderForwardingHandler>();

                if (apiSettings.RequestTokenExchange)
                {
                    // Frontend clients: Use exchange flow
                    if (typeof(TClientInterface) == typeof(ITokensClient))
                    {
                        // Tokens client always uses Azure token (for exchange endpoint authentication)
                        builder.AddHttpMessageHandler<AzureBearerTokenHandler>();

                        // Add auto-registration handler BEFORE token exchange
                        // This intercepts "user not found" errors and auto-registers the user
                        if (apiSettings.AutoRegisterUsers)
                        {
                            builder.AddHttpMessageHandler<UserAutoRegistrationHandler>();
                        }
                    }
                    else
                    {
                        // Other clients use token exchange to get internal tokens
                        builder.AddHttpMessageHandler<TokenExchangeHandler>();
                    }
                }
                else
                {
                    // Service clients: Use Azure token for everything (no exchange needed)
                    builder.AddHttpMessageHandler<AzureBearerTokenHandler>();
                }
            }

            return services;
        }

        /// <summary>
        /// Extension method to register the new token management middleware
        /// </summary>
        public static IApplicationBuilder UseTokenManagementMiddleware(this IApplicationBuilder app)
        {
            return app.UseMiddleware<TokenManagementMiddleware>();
        }




    }


}