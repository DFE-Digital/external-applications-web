using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Web.Authentication;

[ExcludeFromCodeCoverage]
public class TestAuthenticationHandler : AuthenticationHandler<TestAuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuthentication";
    
    private static class SessionKeys
    {
        public const string Email = "TestAuth:Email";
        public const string Token = "TestAuth:Token";
    }

    private static class TokenNames
    {
        public const string IdToken = "id_token";
        public const string AccessToken = "access_token";
    }

    public TestAuthenticationHandler(
        IOptionsMonitor<TestAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var requestPath = Context.Request.Path;
        Logger.LogDebug(">>>>>>>>>> Authentication >>> TestAuthenticationHandler: HandleAuthenticateAsync called for path {Path}", requestPath);
        
        var email = Context.Session.GetString(SessionKeys.Email);
        var token = Context.Session.GetString(SessionKeys.Token);

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            Logger.LogDebug(">>>>>>>>>> Authentication >>> TestAuthenticationHandler: No email ({EmailExists}) or token ({TokenExists}) found in session for path {Path}", 
                !string.IsNullOrEmpty(email), !string.IsNullOrEmpty(token), requestPath);
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        Logger.LogDebug(">>>>>>>>>> Authentication >>> TestAuthenticationHandler: Creating authentication ticket for user {Email} at path {Path}", email, requestPath);
        
        var claims = CreateUserClaims(email);
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, CreateAuthenticationProperties(token), SchemeName);

        Logger.LogDebug(">>>>>>>>>> Authentication >>> TestAuthenticationHandler: Authentication successful for user {Email} with {ClaimCount} claims", 
            email, claims.Count());

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var returnUrl = properties?.RedirectUri;
        var loginUrl = string.IsNullOrEmpty(returnUrl) 
            ? "/TestLogin" 
            : $"/TestLogin?returnUrl={Uri.EscapeDataString(returnUrl)}";
            
        Logger.LogDebug(">>>>>>>>>> Authentication >>> TestAuthenticationHandler: HandleChallengeAsync triggered. Redirecting to {LoginUrl} (return URL: {ReturnUrl})", 
            loginUrl, returnUrl ?? "None");
            
        Response.Redirect(loginUrl);
        return Task.CompletedTask;
    }

    private static IEnumerable<Claim> CreateUserClaims(string email)
    {
        return new[]
        {
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.NameIdentifier, email),
            new Claim("sub", email),
            new Claim("email", email)
        };
    }

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

[ExcludeFromCodeCoverage]
public class TestAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    // Options class for future extensibility
} 