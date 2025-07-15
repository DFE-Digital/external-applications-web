using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using DfE.CoreLibs.Security.Interfaces;
using DfE.CoreLibs.Security.Configurations;
using System.Security.Claims;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Web.Services;

[ExcludeFromCodeCoverage]
public class TestAuthenticationService : ITestAuthenticationService
{
    private readonly IUserTokenService _userTokenService;
    private readonly TestAuthenticationOptions _options;
    private readonly ILogger<TestAuthenticationService> _logger;
    
    private static class SessionKeys
    {
        public const string Email = "TestAuth:Email";
        public const string Token = "TestAuth:Token";
    }

    public TestAuthenticationService(
        IUserTokenService userTokenService,
        IOptions<TestAuthenticationOptions> options,
        ILogger<TestAuthenticationService> logger)
    {
        _userTokenService = userTokenService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TestAuthenticationResult> AuthenticateAsync(string email, HttpContext httpContext)
    {
        if (!_options.Enabled)
        {
            return TestAuthenticationResult.Failure("Test authentication is not enabled.");
        }

        try
        {
            var claims = CreateUserClaims(email);
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            // Generate test token using the existing UserTokenService
            var testToken = await _userTokenService.GetUserTokenAsync(principal);
            
            // Store in session for TestAuthenticationHandler
            httpContext.Session.SetString(SessionKeys.Email, email);
            httpContext.Session.SetString(SessionKeys.Token, testToken);

            // Create authentication properties with stored tokens
            var authProperties = new AuthenticationProperties();
            authProperties.StoreTokens(new[]
            {
                new AuthenticationToken { Name = "id_token", Value = testToken },
                new AuthenticationToken { Name = "access_token", Value = testToken }
            });

            // Sign in using cookie authentication
            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

            _logger.LogInformation("Test authentication successful for email: {Email}", email);

            return TestAuthenticationResult.Success("/applications/dashboard");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during test authentication for email: {Email}", email);
            return TestAuthenticationResult.Failure("An error occurred during authentication. Please try again.");
        }
    }

    public async Task SignOutAsync(HttpContext httpContext)
    {
        // Clear test authentication session data
        httpContext.Session.Remove(SessionKeys.Email);
        httpContext.Session.Remove(SessionKeys.Token);

        // Sign out from cookie authentication if signed in
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        _logger.LogInformation("Test authentication session cleared");
    }

    private static IEnumerable<Claim> CreateUserClaims(string email)
    {
        return new[]
        {
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.NameIdentifier, email),
            new Claim(ClaimTypes.Name, email),
            new Claim("sub", email),
            new Claim("email", email)
        };
    }
} 