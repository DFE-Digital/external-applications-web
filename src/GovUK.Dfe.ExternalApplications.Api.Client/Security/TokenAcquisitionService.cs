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

            _logger.LogDebug(">>>>>>>>>> Authentication >>> TokenAcquisitionService constructor called with ClientId: {ClientId}, Authority: {Authority}, Scope: {Scope}", 
                _settings.ClientId, _settings.Authority, _settings.Scope);

            _app = new Lazy<IConfidentialClientApplication>(() =>
            {
                _logger.LogDebug(">>>>>>>>>> Authentication >>> Creating ConfidentialClientApplication with ClientId: {ClientId}, Authority: {Authority}", 
                    _settings.ClientId, _settings.Authority);
                
                try
                {
                    var app = ConfidentialClientApplicationBuilder.Create(_settings.ClientId)
                        .WithClientSecret(_settings.ClientSecret)
                        .WithAuthority(new Uri(_settings.Authority!))
                        .Build();
                    
                    _logger.LogInformation(">>>>>>>>>> Authentication >>> ConfidentialClientApplication created successfully for ClientId: {ClientId}", 
                        _settings.ClientId);
                    
                    return app;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ">>>>>>>>>> Authentication >>> Failed to create ConfidentialClientApplication for ClientId: {ClientId}, Authority: {Authority}", 
                        _settings.ClientId, _settings.Authority);
                    throw;
                }
            });
        }

        public async Task<string> GetTokenAsync()
        {
            _logger.LogDebug(">>>>>>>>>> Authentication >>> GetTokenAsync called for scope: {Scope}", _settings.Scope);
            
            try
            {
                _logger.LogDebug(">>>>>>>>>> Authentication >>> Starting token acquisition for ClientId: {ClientId}, Scope: {Scope}", 
                    _settings.ClientId, _settings.Scope);

                var authResult = await _app.Value.AcquireTokenForClient(new[] { _settings.Scope })
                    .ExecuteAsync();

                if (authResult == null)
                {
                    _logger.LogError(">>>>>>>>>> Authentication >>> Token acquisition returned null result for ClientId: {ClientId}, Scope: {Scope}", 
                        _settings.ClientId, _settings.Scope);
                    throw new InvalidOperationException("Token acquisition returned null result");
                }

                if (string.IsNullOrEmpty(authResult.AccessToken))
                {
                    _logger.LogError(">>>>>>>>>> Authentication >>> Token acquisition returned empty access token for ClientId: {ClientId}, Scope: {Scope}", 
                        _settings.ClientId, _settings.Scope);
                    throw new InvalidOperationException("Token acquisition returned empty access token");
                }

                _logger.LogInformation(">>>>>>>>>> Authentication >>> Token acquired successfully for ClientId: {ClientId}, Scope: {Scope}, ExpiresOn: {ExpiresOn}, TokenSource: {TokenSource}", 
                    _settings.ClientId, _settings.Scope, authResult.ExpiresOn, authResult.AuthenticationResultMetadata?.TokenSource);

                _logger.LogDebug(">>>>>>>>>> Authentication >>> Token details - TokenType: {TokenType}, IdToken: {HasIdToken}, Account: {HasAccount}", 
                    authResult.TokenType, !string.IsNullOrEmpty(authResult.IdToken), authResult.Account != null);

                return authResult.AccessToken;
            }
            catch (MsalServiceException ex)
            {
                _logger.LogError(ex, ">>>>>>>>>> Authentication >>> MSAL Service Exception during token acquisition - ErrorCode: {ErrorCode}, Claims: {Claims}, CorrelationId: {CorrelationId}, ClientId: {ClientId}, Scope: {Scope}", 
                    ex.ErrorCode, ex.Claims, ex.CorrelationId, _settings.ClientId, _settings.Scope);
                throw;
            }
            catch (MsalClientException ex)
            {
                _logger.LogError(ex, ">>>>>>>>>> Authentication >>> MSAL Client Exception during token acquisition - ErrorCode: {ErrorCode}, ClientId: {ClientId}, Scope: {Scope}", 
                    ex.ErrorCode, _settings.ClientId, _settings.Scope);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ">>>>>>>>>> Authentication >>> Unexpected exception during token acquisition for ClientId: {ClientId}, Scope: {Scope}", 
                    _settings.ClientId, _settings.Scope);
                throw;
            }
        }
    }
}
