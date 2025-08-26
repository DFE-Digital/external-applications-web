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
        _logger.LogDebug(">>>>>>>>>> Authentication >>> TestAuthenticationService: Authentication requested for email {Email}", email);
        
        if (!_options.Enabled)
        {
            _logger.LogWarning(">>>>>>>>>> Authentication >>> TestAuthenticationService: Test authentication is not enabled, rejecting authentication for {Email}", email);
            return TestAuthenticationResult.Failure("Test authentication is not enabled.");
        }

        try
        {
            _logger.LogDebug(">>>>>>>>>> Authentication >>> TestAuthenticationService: Creating claims for user {Email}", email);
            var claims = CreateUserClaims(email);
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            _logger.LogDebug(">>>>>>>>>> Authentication >>> TestAuthenticationService: Generating test token for user {Email}", email);
            // Generate test token using the existing UserTokenService
            var testToken = await _userTokenService.GetUserTokenAsync(principal);
            
            _logger.LogDebug(">>>>>>>>>> Authentication >>> TestAuthenticationService: Token generated successfully, storing in session for user {Email}", email);
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

            _logger.LogDebug(">>>>>>>>>> Authentication >>> TestAuthenticationService: Signing in user {Email} with cookie authentication", email);
            // Sign in using cookie authentication
            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

            _logger.LogInformation(">>>>>>>>>> Authentication >>> TestAuthenticationService: Test authentication successful for email: {Email}", email);

            return TestAuthenticationResult.Success("/applications/dashboard");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ">>>>>>>>>> Authentication >>> TestAuthenticationService: Error during test authentication for email: {Email}", email);
            return TestAuthenticationResult.Failure("An error occurred during authentication. Please try again.");
        }
    }

    public async Task SignOutAsync(HttpContext httpContext)
    {
        var userId = httpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "Unknown";
        _logger.LogDebug(">>>>>>>>>> Authentication >>> TestAuthenticationService: SignOut requested for user {UserId}", userId);
        
        // Clear test authentication session data
        httpContext.Session.Remove(SessionKeys.Email);
        httpContext.Session.Remove(SessionKeys.Token);
        
        _logger.LogDebug(">>>>>>>>>> Authentication >>> TestAuthenticationService: Cleared session data for user {UserId}", userId);

        // Sign out from cookie authentication if signed in
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        _logger.LogInformation(">>>>>>>>>> Authentication >>> TestAuthenticationService: Test authentication session cleared for user {UserId}", userId);
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