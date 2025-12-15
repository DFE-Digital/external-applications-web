using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using DfE.ExternalApplications.Web.Services;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Web.Pages;

[ExcludeFromCodeCoverage]
[AllowAnonymous]
public class LogoutModel : PageModel
{
    private readonly TestAuthenticationOptions _testAuthOptions;
    private readonly ITestAuthenticationService? _testAuthenticationService;
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(
        IOptions<TestAuthenticationOptions> testAuthOptions,
        ILogger<LogoutModel> logger,
        ITestAuthenticationService? testAuthenticationService = null)
    {
        _testAuthOptions = testAuthOptions.Value;
        _testAuthenticationService = testAuthenticationService;
        _logger = logger;
    }

    public IActionResult OnGet()
    {
        // Only show the page if user is authenticated
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return RedirectToPage("/Applications/Dashboard");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            if (_testAuthOptions.Enabled && _testAuthenticationService != null)
            {
                _logger.LogInformation("Signing out from test authentication");
                await _testAuthenticationService.SignOutAsync(HttpContext);
            }
            else
            {
                _logger.LogInformation("Signing out from production authentication");
                
                // Clear the existing external cookie
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                
                // Clear the existing OIDC cookie and redirect to DfE Sign-in for sign out
                await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
                {
                    RedirectUri = Url.Page("/Applications/Dashboard")
                });
            }

            // Clear session data
            HttpContext.Session.Clear();

            _logger.LogInformation("User successfully signed out");
            return RedirectToPage("/Applications/Dashboard");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during sign out process");
            ModelState.AddModelError(string.Empty, "An error occurred while signing out. Please try again.");
            return Page();
        }
    }
} 