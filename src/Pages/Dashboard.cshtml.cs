using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using DfE.ExternalApplications.Web.Models;

namespace DfE.ExternalApplications.Web.Pages
{
    public class DashboardModel : PageModel
    {
        private readonly ILogger<DashboardModel> _logger;
        public string? Email { get; private set; }
        public string? FirstName { get; private set; }
        public string? LastName { get; private set; }
        public string? OrganisationName { get; private set; }
        public List<Application> Applications { get; set; } = new List<Application>();


        public DashboardModel(ILogger<DashboardModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            LoadApplications();
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


        private void LoadApplications()
        {
            var json = @"
              [
                {
                    ""referenceNumber"": ""240315-XYZ45"",
                    ""dateStarted"": ""2025-03-15"",
                    ""status"": ""Not submitted""
                }
              ]";

            Applications = JsonSerializer.Deserialize<List<Application>>(json);
        }

    }
}