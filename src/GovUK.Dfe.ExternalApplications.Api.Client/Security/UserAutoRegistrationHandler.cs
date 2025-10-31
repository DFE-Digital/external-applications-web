using System;
using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using System.Net.Http.Headers;
using GovUK.Dfe.ExternalApplications.Api.Client.Settings;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security;

/// <summary>
/// Automatically registers users when they authenticate with an external IDP for the first time.
/// Intercepts "User not found" errors from token exchange and creates the user account seamlessly.
/// </summary>
[ExcludeFromCodeCoverage]
public class UserAutoRegistrationHandler : DelegatingHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITokenStateManager _tokenStateManager;
    private readonly ITokenAcquisitionService _tokenAcquisitionService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ApiClientSettings _settings;
    private readonly ILogger<UserAutoRegistrationHandler> _logger;
    private readonly SemaphoreSlim _registrationLock = new(1, 1);

    public UserAutoRegistrationHandler(
        IHttpClientFactory httpClientFactory,
        ITokenStateManager tokenStateManager,
        ITokenAcquisitionService tokenAcquisitionService,
        IHttpContextAccessor httpContextAccessor,
        ApiClientSettings settings,
        ILogger<UserAutoRegistrationHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenStateManager = tokenStateManager;
        _tokenAcquisitionService = tokenAcquisitionService;
        _httpContextAccessor = httpContextAccessor;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // If auto-registration is disabled, just pass through
        if (!_settings.AutoRegisterUsers)
        {
            _logger.LogWarning(">>>>>>>>>>> AutoRegistration disabled!");

            return await base.SendAsync(request, cancellationToken);
        }

        _logger.LogWarning(">>>>>>>>>>> AutoRegistration enabled!");

        // First attempt - try the request
        var response = await base.SendAsync(request, cancellationToken);

        // Check if this is a "User not found" error from token exchange
        if (IsUserNotFoundError(response, request))
        {
            _logger.LogInformation("User not found during token exchange. Attempting auto-registration...");

            // Use semaphore to prevent duplicate registrations from concurrent requests
            await _registrationLock.WaitAsync(cancellationToken);
            try
            {
                // Try to auto-register the user
                var registered = await TryAutoRegisterUserAsync(cancellationToken);

                if (registered)
                {
                    _logger.LogInformation("User auto-registered successfully. Retrying original request...");

                    // Clone and retry the original request now that user exists
                    var retryRequest = await CloneRequestAsync(request);
                    response = await base.SendAsync(retryRequest, cancellationToken);
                }
                else
                {
                    _logger.LogWarning("User auto-registration failed. Returning original error response.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user auto-registration. Returning original error response.");
            }
            finally
            {
                _registrationLock.Release();
            }
        }

        return response;
    }

    private bool IsUserNotFoundError(HttpResponseMessage response, HttpRequestMessage request)
    {
        // Only intercept errors from token exchange endpoint
        if (!request.RequestUri?.AbsolutePath.Contains("/tokens/exchange", StringComparison.OrdinalIgnoreCase) == true)
        {
            return false;
        }

        // Check for 403 Forbidden or 404 Not Found (typical for user not found)
        if (response.StatusCode != HttpStatusCode.Forbidden &&
            response.StatusCode != HttpStatusCode.InternalServerError &&
            response.StatusCode != HttpStatusCode.NotFound)
        {
            return false;
        }

        // Try to read the error message to confirm it's a "user not found" error
        try
        {
            var contentTask = response.Content.ReadAsStringAsync();
            contentTask.Wait(); // Synchronous wait is safe here as content is already loaded
            var content = contentTask.Result;

            if (string.IsNullOrEmpty(content))
                return false;

            // Check if error message indicates user not found
            return content.Contains("user", StringComparison.OrdinalIgnoreCase) &&
                   content.Contains("user not found", StringComparison.OrdinalIgnoreCase) &&
                   (content.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                   content.Contains("resource not found", StringComparison.OrdinalIgnoreCase) ||
                   content.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryAutoRegisterUserAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Get the current token state
            var tokenState = await _tokenStateManager.GetCurrentTokenStateAsync();

            // Ensure we have a valid external IDP token
            if (!tokenState.ExternalIdpToken.IsValid || string.IsNullOrEmpty(tokenState.ExternalIdpToken.Value))
            {
                _logger.LogWarning("Cannot auto-register user: External IDP token is missing or invalid");
                return false;
            }

            if (!IsEducationIssuer(tokenState.ExternalIdpToken.Value))
            {
                _logger.LogWarning("Cannot auto-register user: Token issuer does not contain 'education'. Auto-registration is only allowed for education identity providers.");
                return false;
            }

            // Get the template ID from session or fall back to default configuration
            var templateId = GetTemplateIdFromSessionOrConfig();
            if (!templateId.HasValue)
            {
                _logger.LogWarning("Cannot auto-register user: TemplateId is not available in session or configuration");
                return false;
            }

            // Get Azure AD token (service-to-service auth, not OBO)
            var azureToken = await _tokenAcquisitionService.GetTokenAsync();
            if (string.IsNullOrEmpty(azureToken))
            {
                _logger.LogWarning("Cannot auto-register user: Unable to acquire Azure AD token");
                return false;
            }

            _logger.LogInformation("Auto-registering user with TemplateId: {TemplateId} (Source: {Source})", 
                templateId.Value, 
                _httpContextAccessor.HttpContext?.Session.GetString("TemplateId") != null ? "Session" : "Configuration");

            // Create the registration request
            var registerRequest = new RegisterUserRequest
            {
                AccessToken = tokenState.ExternalIdpToken.Value,
                TemplateId = templateId.Value
            };

            // Call the register endpoint using a local client with Azure token only for this request
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_settings.BaseUrl!);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", azureToken);

            var usersClient = new UsersClient(_settings.BaseUrl!, client);
            var result = await usersClient.RegisterUserAsync(registerRequest, cancellationToken);

            _logger.LogInformation("User auto-registered successfully: {UserId} - {Email}", 
                result.UserId, result.Email);

            return true;
        }
        catch (ExternalApplicationsException<ExceptionResponse> ex)
        {
            _logger.LogError(ex, "Auto-registration failed with API error: {StatusCode} - {Message}", 
                ex.StatusCode, ex.Result?.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during user auto-registration");
            return false;
        }
    }

    private Guid? GetTemplateIdFromSessionOrConfig()
    {
        try
        {
            // Try to get TemplateId from session first (higher priority)
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.Session != null)
            {
                var sessionTemplateId = httpContext.Session.GetString("TemplateId");
                if (!string.IsNullOrEmpty(sessionTemplateId) && Guid.TryParse(sessionTemplateId, out var parsedGuid))
                {
                    _logger.LogDebug("Using TemplateId from session: {TemplateId}", parsedGuid);
                    return parsedGuid;
                }
            }

            // Fall back to configuration
            if (_settings.DefaultTemplateId.HasValue)
            {
                _logger.LogDebug("Using DefaultTemplateId from configuration: {TemplateId}", _settings.DefaultTemplateId.Value);
                return _settings.DefaultTemplateId.Value;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving TemplateId from session or configuration");
            return _settings.DefaultTemplateId;
        }
    }

    private bool IsEducationIssuer(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            
            // Check if the token can be read (basic validation)
            if (!handler.CanReadToken(token))
            {
                _logger.LogWarning("Cannot read external IDP token for issuer validation");
                return false;
            }

            var jwtToken = handler.ReadJwtToken(token);
            var issuer = jwtToken.Issuer;

            if (string.IsNullOrEmpty(issuer))
            {
                _logger.LogWarning("External IDP token does not contain an issuer claim");
                return false;
            }

            // Check if issuer contains "education" (case-insensitive)
            var isEducationIssuer = issuer.Contains("education", StringComparison.OrdinalIgnoreCase);

            _logger.LogInformation("Token issuer validation: {Issuer} - IsEducation: {IsEducation}", 
                issuer, isEducationIssuer);

            return isEducationIssuer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token issuer");
            return false;
        }
    }

    private async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        // Copy headers
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Copy content if present
        if (request.Content != null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(contentBytes);

            // Copy content headers
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}

