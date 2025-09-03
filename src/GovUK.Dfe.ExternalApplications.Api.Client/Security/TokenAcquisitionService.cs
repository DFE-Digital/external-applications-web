using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using GovUK.Dfe.ExternalApplications.Api.Client.Settings;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security
{
    [ExcludeFromCodeCoverage]
    public class TokenAcquisitionService : ITokenAcquisitionService
    {
        private readonly ApiClientSettings _settings;
        private readonly ILogger<TokenAcquisitionService> _logger;
        private readonly Lazy<IConfidentialClientApplication> _app;

        public TokenAcquisitionService(ApiClientSettings settings, ILogger<TokenAcquisitionService> logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _app = new Lazy<IConfidentialClientApplication>(() =>
            {
                try
                {
                    var app = ConfidentialClientApplicationBuilder.Create(_settings.ClientId)
                        .WithClientSecret(_settings.ClientSecret)
                        .WithAuthority(new Uri(_settings.Authority!))
                        .Build();
                    
                    return app;
                }
                catch (Exception ex)
                {
                    throw;
                }
            });
        }

        public async Task<string> GetTokenAsync()
        {
            try
            {
                var authResult = await _app.Value.AcquireTokenForClient(new[] { _settings.Scope })
                    .ExecuteAsync();

                if (authResult == null)
                {
                    throw new InvalidOperationException("Token acquisition returned null result");
                }

                if (string.IsNullOrEmpty(authResult.AccessToken))
                {
                    throw new InvalidOperationException("Token acquisition returned empty access token");
                }

                return authResult.AccessToken;
            }
            catch (MsalServiceException ex)
            {
                throw;
            }
            catch (MsalClientException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }
}
