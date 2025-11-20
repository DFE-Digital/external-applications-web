using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DfE.ExternalApplications.Web.Services;

namespace DfE.ExternalApplications.Web.Authentication;

/// <summary>
/// Authentication handler for internal service authentication
/// Follows the same pattern as TestAuthenticationHandler but reads from headers instead of session
/// Implements forwarder pattern - only engages when service headers are present
/// </summary>
public class InternalServiceAuthenticationHandler(
    IOptionsMonitor<InternalServiceAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISystemClock clock,
    IInternalServiceAuthenticationService internalServiceAuth) : AuthenticationHandler<InternalServiceAuthenticationSchemeOptions>(options, logger, encoder, clock)
{
    public const string SchemeName = "InternalServiceAuth";
    
    private static class HeaderNames
    {
        public const string ServiceEmail = "X-Service-Email";
        public const string ServiceApiKey = "X-Service-Api-Key";
        public const string ServiceToken = "X-Service-Token";
    }
    
    private static class TokenNames
    {
        public const string IdToken = "id_token";
        public const string AccessToken = "access_token";
    }
    
    private readonly IInternalServiceAuthenticationService _internalServiceAuth = internalServiceAuth;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var path = Context.Request.Path;
        
        // FORWARDER PATTERN: Only engage this handler if the service header is present
        // This is a performance optimization - avoid unnecessary processing
        var serviceEmail = Context.Request.Headers[HeaderNames.ServiceEmail].FirstOrDefault();
        if (string.IsNullOrEmpty(serviceEmail))
        {
            // No service header = not an internal service request
            // Return NoResult immediately without logging (better performance)
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        Logger.LogDebug("InternalServiceAuth checking request for {Email} on {Path}", serviceEmail, path);

        // Get API key and token from headers
        var apiKey = Context.Request.Headers[HeaderNames.ServiceApiKey].FirstOrDefault();
        var serviceToken = Context.Request.Headers[HeaderNames.ServiceToken].FirstOrDefault();

        // Validate all required headers are present
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(serviceToken))
        {
            Logger.LogWarning(
                "InternalServiceAuth missing required headers for {Email}. HasApiKey: {HasApiKey}, HasToken: {HasToken}",
                serviceEmail, !string.IsNullOrEmpty(apiKey), !string.IsNullOrEmpty(serviceToken));
            return Task.FromResult(AuthenticateResult.Fail("Missing required authentication headers"));
        }

        // SECURITY: Validate service credentials (email + API key)
        if (!_internalServiceAuth.ValidateServiceCredentials(serviceEmail, apiKey))
        {
            Logger.LogWarning(
                "Unauthorized service authentication attempt: {Email} from {IP}", 
                serviceEmail, 
                Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid service credentials"));
        }

        // Create claims and ticket (exactly like Test Auth)
        var claims = CreateServiceClaims(serviceEmail);
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, CreateAuthenticationProperties(serviceToken), SchemeName);

        Logger.LogInformation(
            "InternalServiceAuth successful for {Email} on path {Path}",
            serviceEmail, path);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    // Exactly like Test Auth
    private static IEnumerable<Claim> CreateServiceClaims(string serviceEmail)
    {
        return new[]
        {
            new Claim(ClaimTypes.Name, serviceEmail),
            new Claim(ClaimTypes.Email, serviceEmail),
            new Claim(ClaimTypes.NameIdentifier, serviceEmail),
            new Claim("sub", serviceEmail),
            new Claim("email", serviceEmail),
            new Claim("service_type", "internal")
        };
    }

    // Exactly like Test Auth
    private static AuthenticationProperties CreateAuthenticationProperties(string token)
    {
        var properties = new AuthenticationProperties();
        properties.StoreTokens(new[]
        {
            new AuthenticationToken { Name = TokenNames.IdToken, Value = token },
            new AuthenticationToken { Name = TokenNames.AccessToken, Value = token }
        });
        return properties;
    }
}

/// <summary>
/// Options for internal service authentication scheme
/// </summary>
public class InternalServiceAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    // Options class for future extensibility
}

