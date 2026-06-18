using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DfE.ExternalApplications.Web.Pages.Applications;

[Authorize(Roles = "Admin, Caseworker")]
public class IndexModel : PageModel
{
    public IEnumerable<Application> Applications { get; private set; } = [];

    public void OnGet()
    {
        var templateId = Request.Query["templateid"]; // TODO get this from razor page?

        // HACK get applicationss from API! - remove Microsoft.Data.SqlClient nuget package reference
    }
}

public class Application
{
    public string? Reference { get; set; }
    public int? Status { get; set; }
}
