using System;
using System.Threading.Tasks;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace DfE.ExternalApplications.Web.Controllers
{
    [Authorize]
    [Route("session")]
    public class SessionController(ITokenStateManager tokenStateManager, ICacheManager cacheManager)
        : Controller
    {
        [HttpPost("stay-signed-in")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StaySignedIn()
        {
            cacheManager.SetRequestScopedFlag("AllowRefreshDueToInactivity", true);

            await tokenStateManager.RefreshTokensIfPossibleAsync();

            return Redirect("/applications/dashboard");
        }

        [HttpPost("sign-out")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SignOutImmediately()
        {
            await tokenStateManager.ForceCompleteLogoutAsync();

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            var authScheme = User?.Identity?.AuthenticationType;
            var usingOidc = string.Equals(authScheme, "AuthenticationTypes.Federation", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(authScheme, OpenIdConnectDefaults.AuthenticationScheme, StringComparison.OrdinalIgnoreCase);
            if (usingOidc)
            {
                try
                {
                    await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
                    {
                        RedirectUri = "/"
                    });
                }
                catch
                {
                    // ignore if OIDC not configured in current environment
                }
            }

            HttpContext.Session.Clear();

            return Redirect("/");
        }
    }
}


