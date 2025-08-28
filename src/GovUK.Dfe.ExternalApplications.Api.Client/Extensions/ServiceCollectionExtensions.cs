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
            services.AddTransient<AzureBearerTokenHandler>();
            
            if (apiSettings.RequestTokenExchange)
            {
                // Frontend clients need internal token storage and exchange handler
                services.AddScoped<IInternalUserTokenStore, CachedInternalUserTokenStore>();
                
                // NEW CLEAN ARCHITECTURE - Register core services
                services.AddScoped<ICacheManager, DistributedCacheManager>();
                services.AddScoped<ITokenStateManager, TokenStateManager>();
                
                // NOTE: Authentication strategies are now registered in consuming applications
                // This allows each application to implement their own authentication logic
                // and removes coupling from this reusable library
                
                // Register backward-compatible service that delegates to new architecture
                services.AddScoped<ITokenExpiryService, TokenExpiryService>();
                
                // Ensure distributed cache is available for logout tracking
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

                if (apiSettings.RequestTokenExchange)
                {
                    // Frontend clients: Use exchange flow
                    if (typeof(TClientInterface) == typeof(ITokensClient))
                    {
                        // Tokens client always uses Azure token (for exchange endpoint authentication)
                        builder.AddHttpMessageHandler<AzureBearerTokenHandler>();
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

        /// <summary>
        /// DEPRECATED: Use UseTokenManagementMiddleware instead
        /// Adds token expiry middleware for proactive token expiry checking
        /// </summary>
        /// <param name="app">The application builder</param>
        /// <returns>The application builder for chaining</returns>
        /// <remarks>
        /// This middleware should be added after authentication middleware but before any middleware
        /// that requires token validation. It will proactively check for token expiry and handle
        /// expired tokens appropriately.
        /// </remarks>
        public static IApplicationBuilder UseTokenExpiryMiddleware(this IApplicationBuilder app)
        {
            // For backward compatibility, delegate to new middleware
            return app.UseTokenManagementMiddleware();
        }

        /// <summary>
        /// Extension method to check if tokens are expired in the current request
        /// </summary>
        /// <param name="app">The application builder</param>
        /// <param name="onTokenExpired">Action to execute when tokens are expired</param>
        /// <returns>The application builder for chaining</returns>
        /// <remarks>
        /// This can be used in custom middleware or controllers to handle token expiry
        /// </remarks>
        public static IApplicationBuilder UseTokenExpiryHandler(
            this IApplicationBuilder app, 
            Action<HttpContext, TokenExpiryInfo> onTokenExpired)
        {
            return app.Use(async (context, next) =>
            {
                // Check if token expiry middleware has flagged tokens as expired
                if (context.Items.TryGetValue("TokenExpired", out var tokenExpired) && 
                    tokenExpired is true &&
                    context.Items.TryGetValue("TokenExpiryInfo", out var expiryInfo) &&
                    expiryInfo is TokenExpiryInfo info)
                {
                    onTokenExpired(context, info);
                    return;
                }

                await next();
            });
        }
    }

    /// <summary>
    /// Extension methods for token management in controllers and pages
    /// </summary>
    [ExcludeFromCodeCoverage]
    public static class TokenManagementExtensions
    {
        /// <summary>
        /// Checks if any user tokens are expired or will expire soon
        /// </summary>
        /// <param name="serviceProvider">Service provider to resolve dependencies</param>
        /// <returns>True if tokens are expired or will expire within the buffer time</returns>
        public static bool AreTokensExpired(this IServiceProvider serviceProvider)
        {
            var tokenExpiryService = serviceProvider.GetService<ITokenExpiryService>();
            return tokenExpiryService?.IsAnyTokenExpired() ?? true;
        }

        /// <summary>
        /// Gets comprehensive token expiry information
        /// </summary>
        /// <param name="serviceProvider">Service provider to resolve dependencies</param>
        /// <returns>Token expiry information</returns>
        public static TokenExpiryInfo GetTokenExpiryInfo(this IServiceProvider serviceProvider)
        {
            var tokenExpiryService = serviceProvider.GetService<ITokenExpiryService>();
            return tokenExpiryService?.GetTokenExpiryInfo() ?? new TokenExpiryInfo
            {
                IsAnyTokenExpired = true,
                ExpiryReason = "TokenExpiryService not available"
            };
        }

        /// <summary>
        /// Forces logout by clearing all tokens
        /// </summary>
        /// <param name="serviceProvider">Service provider to resolve dependencies</param>
        public static void ForceLogout(this IServiceProvider serviceProvider)
        {
            var tokenExpiryService = serviceProvider.GetService<ITokenExpiryService>();
            tokenExpiryService?.ForceLogout();
        }

        /// <summary>
        /// Forces token refresh by clearing cached tokens
        /// </summary>
        /// <param name="serviceProvider">Service provider to resolve dependencies</param>
        public static void ForceTokenRefresh(this IServiceProvider serviceProvider)
        {
            var tokenStore = serviceProvider.GetService<IInternalUserTokenStore>();
            tokenStore?.ClearToken();
        }

        /// <summary>
        /// Checks if logout is required due to token expiry
        /// </summary>
        /// <param name="serviceProvider">Service provider to resolve dependencies</param>
        /// <returns>True if the application should perform logout/redirect</returns>
        public static bool IsLogoutRequired(this IServiceProvider serviceProvider)
        {
            var tokenExpiryService = serviceProvider.GetService<ITokenExpiryService>();
            return tokenExpiryService?.IsLogoutRequired() ?? false;
        }
    }
}