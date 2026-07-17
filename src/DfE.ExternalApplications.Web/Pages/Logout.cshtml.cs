using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Security.EntraSso;
using DfE.ExternalApplications.Web.Security;
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

            // Sign out only the remote scheme. Its SignOutScheme clears the auth cookie after
            // the IdP round-trip, which preserves the OIDC correlation cookie needed for
            // /signout-callback-oidc (or Entra equivalent). Clearing cookies first causes
            // "Correlation failed" when the IdP returns.
            var signOutProperties = new AuthenticationProperties { RedirectUri = "/" };

            if (TenantAuthSchemeSelector.IsEntraSsoEnabled(HttpContext, entraSsoOptions))
            {
                logger.LogInformation("Signing out from Entra SSO authentication");

                return SignOut(
                    signOutProperties,
                    EntraSsoDefaults.AuthenticationScheme);
            }

            logger.LogInformation("Signing out from DfE Sign-In OIDC authentication");

            return SignOut(
                signOutProperties,
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
