using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;

namespace DfE.ExternalApplications.Web.Pages
{
    public class DashboardModel : PageModel
    {
        private readonly ILogger<DashboardModel> _logger;
        public string? Email { get; private set; }
        public string? FirstName { get; private set; }
        public string? LastName { get; private set; }
        public string? OrganisationName { get; private set; }


        public DashboardModel(ILogger<DashboardModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            Email = User.FindFirst(ClaimTypes.Email)?.Value
                    ?? User.FindFirst("email")?.Value;

            FirstName = User.FindFirst(ClaimTypes.GivenName)?.Value;
            LastName = User.FindFirst(ClaimTypes.Surname)?.Value;

            var orgJson = User.FindFirst("organisation")?.Value;
            if (!string.IsNullOrEmpty(orgJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(orgJson);
                    OrganisationName = doc.RootElement
                        .GetProperty("name")
                        .GetString();
                }
                catch
                {
                    OrganisationName = null;
                }
            }

        }
    }
    }