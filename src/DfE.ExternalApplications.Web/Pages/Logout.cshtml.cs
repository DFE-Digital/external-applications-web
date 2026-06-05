using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Security.EntraSso;
using DfE.ExternalApplications.Web.Services;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Web.Pages;

[ExcludeFromCodeCoverage]
[AllowAnonymous]
public class LogoutModel(
    IOptions<TestAuthenticationOptions> testAuthOptions,
    IOptions<EntraSsoOptions> entraSsoOptions,
    ILogger<LogoutModel> logger,
    ITestAuthenticationService? testAuthenticationService = null) : PageModel
{
    public IActionResult OnGet()
    {
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
            if (testAuthOptions.Value.Enabled && testAuthenticationService != null)
            {
                logger.LogInformation("Signing out from test authentication");
                HttpContext.Session.Clear();
                await testAuthenticationService.SignOutAsync(HttpContext);
                return Redirect("/");
            }

            // Do not clear session or sign out the cookie scheme before OIDC sign-out.
            // The OIDC handler signs out the cookie (SignOutScheme) and preserves correlation
            // state for the /signout-callback-oidc round trip. Clearing early causes 403 on callback.
            var signOutProperties = new AuthenticationProperties { RedirectUri = "/" };

            if (entraSsoOptions.Value.Enabled)
            {
                logger.LogInformation("Signing out from Entra SSO authentication");

                return SignOut(
                    signOutProperties,
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    EntraSsoDefaults.AuthenticationScheme);
            }

            logger.LogInformation("Signing out from DfE Sign-In OIDC authentication");

            return SignOut(
                signOutProperties,
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during sign out process");
            ModelState.AddModelError(string.Empty, "An error occurred while signing out. Please try again.");
            return Page();
        }
    }
} 