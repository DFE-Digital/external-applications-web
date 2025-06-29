using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using System.Text.Json;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using Microsoft.AspNetCore.Authorization;
using DfE.ExternalApplications.Client.Contracts;

namespace DfE.ExternalApplications.Web.Pages
{
    [Authorize]
    public class DashboardModel : PageModel
    {
        private readonly ILogger<DashboardModel> _logger;
        private readonly IApplicationsClient _applicationsClient;

        public string? Email { get; private set; }
        public string? FirstName { get; private set; }
        public string? LastName { get; private set; }
        public string? OrganisationName { get; private set; }
        public IReadOnlyList<ApplicationDto> Applications { get; private set; } = Array.Empty<ApplicationDto>();
        public bool HasError { get; private set; }
        public string? ErrorMessage { get; private set; }

        public DashboardModel(
            ILogger<DashboardModel> logger,
            IApplicationsClient applicationsClient)
        {
            _logger = logger;
            _applicationsClient = applicationsClient;
        }

        public async Task OnGetAsync()
        {
            await LoadUserDetailsAsync();
            await LoadApplicationsAsync();
        }

        private async Task LoadApplicationsAsync()
        {
            try
            {
                var applications = await _applicationsClient.GetMyApplicationsAsync();
                Applications = applications
                    .OrderByDescending(a => a.DateCreated)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load applications for user {Email}", Email);
                HasError = true;
                ErrorMessage = "There was a problem loading your applications. Please try again later.";
                Applications = Array.Empty<ApplicationDto>();
            }
        }

        private Task LoadUserDetailsAsync()
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
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse organisation JSON for user {Email}", Email);
                    OrganisationName = null;
                }
            }

            return Task.CompletedTask;
        }
    }
}