using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DfE.ExternalApplications.Web.Pages.Shared
{
    public class CookiesModel : PageModel
    {
        public void OnGet()
        {
        }
        public IActionResult OnPostHideMessage(string redirectPath)
        {
            HttpContext.Session.SetString("CookieBannerHidden", "1");
            return LocalRedirect(redirectPath);
        }
    }
}
