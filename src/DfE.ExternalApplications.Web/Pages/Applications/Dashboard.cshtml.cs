using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using DfE.CoreLibs.Contracts.ExternalApplications.Enums;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Request;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using DfE.ExternalApplications.Application.Interfaces;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SystemTask = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Web.Pages.Applications
{
    [ExcludeFromCodeCoverage]
    [Authorize]
    public class DashboardModel(
        ILogger<DashboardModel> logger,
        IApplicationsClient applicationsClient,
        IHttpContextAccessor httpContextAccessor,
        IApplicationResponseService applicationResponseService,
        IFormTemplateProvider templateProvider)
        : PageModel
    {
        public string? Email { get; private set; }
        public string? FirstName { get; private set; }
        public string? LastName { get; private set; }
        public string? OrganisationName { get; private set; }
        public IReadOnlyList<ApplicationWithCalculatedStatus> Applications { get; private set; } = Array.Empty<ApplicationWithCalculatedStatus>();
        public bool HasError { get; private set; }
        public string? ErrorMessage { get; private set; }

        public class ApplicationWithCalculatedStatus
        {
            public ApplicationDto Application { get; set; } = null!;
            public ApplicationStatus CalculatedStatus { get; set; }

            // Convenience properties to access original application properties
            public Guid ApplicationId => Application.ApplicationId;
            public string ApplicationReference => Application.ApplicationReference;
            public string TemplateName => Application.TemplateName;
            public DateTime DateCreated => Application.DateCreated;
            public DateTime? DateSubmitted => Application.DateSubmitted;
        }

        public async SystemTask OnGetAsync()
        {
            await LoadUserDetailsAsync();
            await LoadApplicationsAsync();
        }

        /// <summary>
        /// Calculate the actual application status based on response data
        /// </summary>
        public async System.Threading.Tasks.Task<ApplicationStatus> GetCalculatedApplicationStatusAsync(ApplicationDto application)
        {
            try
            {
                // If already submitted, return submitted
                if (application.Status == ApplicationStatus.Submitted)
                {
                    return ApplicationStatus.Submitted;
                }

                // Check if there's any response data indicating progress
                if (application.LatestResponse?.ResponseBody != null)
                {
                    try
                    {
                        // Try to decode base64 first
                        string responseJson;
                        try
                        {
                            var decodedBytes = Convert.FromBase64String(application.LatestResponse.ResponseBody);
                            responseJson = System.Text.Encoding.UTF8.GetString(decodedBytes);
                        }
                        catch
                        {
                            // If base64 decode fails, treat as plain JSON
                            responseJson = application.LatestResponse.ResponseBody;
                        }

                        var responseData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);
                        if (responseData != null && responseData.Any())
                        {
                            // Check if there's any actual field data (not just task status)
                            var hasFieldData = responseData.Any(kvp =>
                                !kvp.Key.StartsWith("TaskStatus_") &&
                                kvp.Value.ValueKind != JsonValueKind.Null &&
                                !string.IsNullOrWhiteSpace(kvp.Value.ToString()));

                            if (hasFieldData)
                            {
                                return ApplicationStatus.InProgress;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to parse response data for application {ApplicationId}", application.ApplicationId);
                    }
                }

                // No response data = InProgress (default state for new applications)
                return ApplicationStatus.InProgress;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to calculate application status for {ApplicationId}, defaulting to InProgress",
                    application.ApplicationId);
                return ApplicationStatus.InProgress;
            }
        }

        public async Task<IActionResult> OnPostCreateApplicationAsync()
        {
            var templateId = HttpContext.Session.GetString("TemplateId") ?? string.Empty;

            var response = await applicationsClient.CreateApplicationAsync(new CreateApplicationRequest
            {
                InitialResponseBody = "{}",
                TemplateId = new Guid(templateId)
            });

            HttpContext.Session.SetString("ApplicationId", response.ApplicationId.ToString());
            HttpContext.Session.SetString("ApplicationReference", response.ApplicationReference);

            // Clear any existing accumulated form data when starting a new application
            applicationResponseService.ClearAccumulatedFormData(HttpContext.Session);
            HttpContext.Session.SetString("CurrentAccumulatedApplicationId", response.ApplicationId.ToString());

            logger.LogInformation("Created new application {ApplicationId} and cleared accumulated form data", response.ApplicationId);

            httpContextAccessor.ForceTokenRefresh();

            return RedirectToPage("/Applications/Contributors", new { referenceNumber = response.ApplicationReference });
        }

        private async SystemTask LoadApplicationsAsync()
        {
            var templateId = HttpContext.Session.GetString("TemplateId") ?? string.Empty;

            var applications = await applicationsClient.GetMyApplicationsAsync(templateId: new Guid(templateId));

            // Calculate status for each application
            var applicationTasks = applications.Select(async app => new ApplicationWithCalculatedStatus
            {
                Application = app,
                CalculatedStatus = await GetCalculatedApplicationStatusAsync(app)
            });

            var applicationsWithStatus = await SystemTask.WhenAll(applicationTasks);

            Applications = applicationsWithStatus
                .OrderByDescending(a => a.DateCreated)
                .ToList();
        }

        private SystemTask LoadUserDetailsAsync()
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

            return SystemTask.CompletedTask;
        }
    }
}