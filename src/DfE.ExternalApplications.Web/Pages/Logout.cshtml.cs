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
            HttpContext.Session.Clear();

            if (testAuthOptions.Value.Enabled && testAuthenticationService != null)
            {
                logger.LogInformation("Signing out from test authentication");
                await testAuthenticationService.SignOutAsync(HttpContext);
                return RedirectToPage("/Applications/Dashboard");
            }

            var redirectUri = Url.Page("/Applications/Dashboard");

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            if (entraSsoOptions.Value.Enabled)
            {
                logger.LogInformation("Signing out from Entra SSO authentication");

                await HttpContext.SignOutAsync(EntraSsoDefaults.AuthenticationScheme, new AuthenticationProperties
                {
                    RedirectUri = redirectUri
                });
            }
            else
            {
                logger.LogInformation("Signing out from DfE Sign-In OIDC authentication");

                await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
                {
                    RedirectUri = redirectUri
                });
            }

            logger.LogInformation("User successfully signed out, redirecting to IdP end-session endpoint");

            // The OIDC SignOutAsync already wrote a 302 redirect to the IdP's
            // end-session endpoint. Returning an empty result preserves that
            // redirect so the browser actually reaches the IdP logout page.
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during sign out process");
            ModelState.AddModelError(string.Empty, "An error occurred while signing out. Please try again.");
            return Page();
        }
    }
} 