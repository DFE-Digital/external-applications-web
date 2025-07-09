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
        var email = Context.Session.GetString("TestAuth:Email");
        var token = Context.Session.GetString("TestAuth:Token");

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.Email, email)
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, CreateAuthenticationProperties(token), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Redirect("/TestLogin");
        return Task.CompletedTask;
    }

    private AuthenticationProperties CreateAuthenticationProperties(string token)
    {
        var properties = new AuthenticationProperties();
        properties.StoreTokens(new[]
        {
            new AuthenticationToken { Name = "id_token", Value = token },
            new AuthenticationToken { Name = "access_token", Value = token }
        });
        return properties;
    }
}

[ExcludeFromCodeCoverage]
public class TestAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    // Empty options class for now, can be extended if needed
} 