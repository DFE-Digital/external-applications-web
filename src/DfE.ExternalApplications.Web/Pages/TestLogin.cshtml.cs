using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using DfE.CoreLibs.Security.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Diagnostics.CodeAnalysis;
using DfE.CoreLibs.Security.Configurations;
using DfE.ExternalApplications.Web.Authentication;

namespace DfE.ExternalApplications.Web.Pages;

[ExcludeFromCodeCoverage]
[AllowAnonymous]
public class TestLoginModel(
    IOptions<TestAuthenticationOptions> testAuthOptions,
    IUserTokenService userTokenService,
    ILogger<TestLoginModel> logger)
    : PageModel
{
    private readonly TestAuthenticationOptions _testAuthOptions = testAuthOptions.Value;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        // Only allow access if test authentication is enabled
        if (!_testAuthOptions.Enabled)
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Only allow access if test authentication is enabled
        if (!_testAuthOptions.Enabled)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            // Create claims for the user
            var claims = new[]
            {
                new Claim(ClaimTypes.Email, Input.Email),
                new Claim(ClaimTypes.NameIdentifier, Input.Email),
                new Claim(ClaimTypes.Name, Input.Email),
                new Claim("sub", Input.Email),
                new Claim("email", Input.Email)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            // Generate test token using the existing UserTokenService
            var testToken = await userTokenService.GetUserTokenAsync(principal);
            
            // Store in session for TestAuthenticationHandler
            HttpContext.Session.SetString("TestAuth:Email", Input.Email);
            HttpContext.Session.SetString("TestAuth:Token", testToken);

            // Create authentication properties with stored tokens
            var authProperties = new AuthenticationProperties();
            authProperties.StoreTokens(new[]
            {
                new AuthenticationToken { Name = "id_token", Value = testToken },
                new AuthenticationToken { Name = "access_token", Value = testToken }
            });

            // Sign in using cookie authentication
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

            logger.LogInformation("Test authentication successful for email: {Email}", Input.Email);

            // Redirect to return URL or default to dashboard
            var redirectUrl = ReturnUrl ?? "/Dashboard";
            return Redirect(redirectUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during test authentication for email: {Email}", Input.Email);
            ErrorMessage = "An error occurred during authentication. Please try again.";
            return Page();
        }
    }

    public class InputModel
    {
        [Required(ErrorMessage = "Email address is required")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address")]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;
    }
} 