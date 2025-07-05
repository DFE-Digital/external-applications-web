using DfE.CoreLibs.Contracts.ExternalApplications.Models.Request;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;

namespace DfE.ExternalApplications.Web.Pages
{
    [ExcludeFromCodeCoverage]
    [Authorize]
    public class DashboardModel(
        ILogger<DashboardModel> logger,
        IApplicationsClient applicationsClient,
        IHttpContextAccessor httpContextAccessor)
        : PageModel
    {
        public string? Email { get; private set; }
        public string? FirstName { get; private set; }
        public string? LastName { get; private set; }
        public string? OrganisationName { get; private set; }
        public IReadOnlyList<ApplicationDto> Applications { get; private set; } = Array.Empty<ApplicationDto>();
        public bool HasError { get; private set; }
        public string? ErrorMessage { get; private set; }

        public async Task OnGetAsync()
        {
            await LoadUserDetailsAsync();
            await LoadApplicationsAsync();
        }

        public async Task<IActionResult> OnPostCreateApplicationAsync()
        {
            try
            {
                var templateId = HttpContext.Session.GetString("TemplateId") ?? string.Empty;

                var response = await applicationsClient.CreateApplicationAsync(new CreateApplicationRequest
                {
                    InitialResponseBody = "{}",
                    TemplateId = new Guid(templateId)
                });

                HttpContext.Session.SetString("ApplicationId", response.ApplicationId.ToString());
                HttpContext.Session.SetString("ApplicationReference", response.ApplicationReference);

                httpContextAccessor.ForceTokenRefresh();

                return RedirectToPage("RenderForm", new { referenceNumber = response.ApplicationReference });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create new application");
                HasError = true;
                ErrorMessage = "There was a problem creating your application. Please try again later.";
                await LoadUserDetailsAsync();
                await LoadApplicationsAsync();
                return Page();
            }
        }

        private async Task LoadApplicationsAsync()
        {
            try
            {
                var applications = await applicationsClient.GetMyApplicationsAsync();
                Applications = applications
                    .OrderByDescending(a => a.DateCreated)
                    .ToList();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load applications for user {Email}", Email);
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
                    logger.LogWarning(ex, "Failed to parse organisation JSON for user {Email}", Email);
                    OrganisationName = null;
                }
            }

            return Task.CompletedTask;
        }
    }
}