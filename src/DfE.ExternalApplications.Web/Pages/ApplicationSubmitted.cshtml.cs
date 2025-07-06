using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Web.Pages
{
    [ExcludeFromCodeCoverage]
    public class ApplicationSubmittedModel : PageModel
    {
        [BindProperty(SupportsGet = true, Name = "referenceNumber")] 
        public string ReferenceNumber { get; set; }

        public void OnGet()
        {
            // Page loads with reference number from route
        }
    }
} 