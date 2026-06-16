using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Application.Options;
using DfE.ExternalApplications.Web.Models.Applications;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SystemTask = System.Threading.Tasks.Task;
using Microsoft.Extensions.Configuration;

namespace DfE.ExternalApplications.Web.Pages.Applications
{
    [ExcludeFromCodeCoverage]
    [Authorize]
    public class DashboardModel(
        ILogger<DashboardModel> logger,
        IApplicationsClient applicationsClient,
        IHttpContextAccessor httpContextAccessor,
        IApplicationResponseService applicationResponseService,
        IFormTemplateProvider templateProvider,
        IOptions<DashboardOptions> dashboardOptions)
        : PageModel
    {
        public string? Email { get; private set; }
        public string? FirstName { get; private set; }
        public string? LastName { get; private set; }
        public string? OrganisationName { get; private set; }
        public IReadOnlyList<ApplicationWithCalculatedStatus> Applications { get; private set; } = Array.Empty<ApplicationWithCalculatedStatus>();
        public bool HasError { get; private set; }
        public string? ErrorMessage { get; private set; }

        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public string? SearchReference { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? DateStartedFrom { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? DateStartedTo { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? DateSubmittedFrom { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? DateSubmittedTo { get; set; }

        [BindProperty(SupportsGet = true)]
        public ApplicationStatus? Status { get; set; }

        public DashboardApplicationSearch SearchFilters => new()
        {
            SearchReference = SearchReference,
            DateStartedFromValue = DateStartedFrom,
            DateStartedToValue = DateStartedTo,
            DateSubmittedFromValue = DateSubmittedFrom,
            DateSubmittedToValue = DateSubmittedTo,
            Status = Status
        };

        public int TotalPages { get; private set; }
        public int PageSize => dashboardOptions.Value.PageSize;
        public bool IsSearchActive => SearchFilters.HasActiveFilters;

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
            ValidateSearchFilters();
            await LoadUserDetailsAsync();
            await LoadApplicationsAsync();
        }

        private void ValidateSearchFilters()
        {
            var filters = SearchFilters;

            if (!string.IsNullOrWhiteSpace(filters.DateStartedFromValue) && !filters.DateStartedFrom.HasValue)
                ModelState.AddModelError(nameof(DateStartedFrom), "Enter a valid date started 'from' date.");

            if (!string.IsNullOrWhiteSpace(filters.DateStartedToValue) && !filters.DateStartedTo.HasValue)
                ModelState.AddModelError(nameof(DateStartedTo), "Enter a valid date started 'to' date.");

            if (!string.IsNullOrWhiteSpace(filters.DateSubmittedFromValue) && !filters.DateSubmittedFrom.HasValue)
                ModelState.AddModelError(nameof(DateSubmittedFrom), "Enter a valid date submitted 'from' date.");

            if (!string.IsNullOrWhiteSpace(filters.DateSubmittedToValue) && !filters.DateSubmittedTo.HasValue)
                ModelState.AddModelError(nameof(DateSubmittedTo), "Enter a valid date submitted 'to' date.");

            if (filters.DateStartedFrom.HasValue && filters.DateStartedTo.HasValue && filters.DateStartedFrom > filters.DateStartedTo)
                ModelState.AddModelError(nameof(DateStartedTo), "Date started 'to' must be on or after date started 'from'.");

            if (filters.DateSubmittedFrom.HasValue && filters.DateSubmittedTo.HasValue && filters.DateSubmittedFrom > filters.DateSubmittedTo)
                ModelState.AddModelError(nameof(DateSubmittedTo), "Date submitted 'to' must be on or after date submitted 'from'.");
        }

        public string BuildPaginationHref(int page) => SearchFilters.BuildPaginationHref(page);

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
            var templateGuid = ResolveTemplateId();
            if (!templateGuid.HasValue)
            {
                HasError = true;
                ErrorMessage = "Template is not configured. Please refresh the page.";
                logger.LogWarning("TemplateId not available when creating application");
                return Page();
            }

            var response = await applicationsClient.CreateApplicationAsync(new CreateApplicationRequest
            {
                InitialResponseBody = "{}",
                TemplateId = templateGuid.Value
            });

            HttpContext.Session.SetString("ApplicationId", response.ApplicationId.ToString());
            HttpContext.Session.SetString("ApplicationReference", response.ApplicationReference);

            // Clear any existing accumulated form data when starting a new application
            applicationResponseService.ClearAccumulatedFormData(HttpContext.Session);
            HttpContext.Session.SetString("CurrentAccumulatedApplicationId", response.ApplicationId.ToString());

            logger.LogInformation("Created new application {ApplicationId} and cleared accumulated form data", response.ApplicationId);

            // Note: Token management now handled automatically by TokenManagementMiddleware
            
            return RedirectToPage("/Applications/Contributors", new { referenceNumber = response.ApplicationReference });
        }

        private async SystemTask LoadApplicationsAsync()
        {
            if (!ModelState.IsValid)
            {
                Applications = Array.Empty<ApplicationWithCalculatedStatus>();
                return;
            }

            var templateGuid = ResolveTemplateId();
            if (!templateGuid.HasValue)
            {
                // Try again on next request; show empty state instead of erroring
                logger.LogWarning("TemplateId not available when loading applications; rendering empty dashboard");
                Applications = Array.Empty<ApplicationWithCalculatedStatus>();
                return;
            }

            var pageSize = dashboardOptions.Value.PageSize;
            var filters = SearchFilters;
            var result = await applicationsClient.GetMyApplicationsAsync(
                templateId: templateGuid.Value,
                pageNumber: CurrentPage,
                pageSize: pageSize,
                applicationReference: string.IsNullOrWhiteSpace(filters.SearchReference) ? null : filters.SearchReference,
                dateStartedFrom: filters.DateStartedFrom,
                dateStartedTo: filters.DateStartedTo,
                dateSubmittedFrom: filters.DateSubmittedFrom,
                dateSubmittedTo: filters.DateSubmittedTo,
                status: filters.Status);

            TotalPages = result.TotalPages;
            CurrentPage = Math.Clamp(CurrentPage, 1, Math.Max(1, TotalPages));

            var applicationTasks = result.Items.AsEnumerable().Select(async app => new ApplicationWithCalculatedStatus
            {
                Application = app,
                CalculatedStatus = await GetCalculatedApplicationStatusAsync(app)
            });

            Applications = [..(await SystemTask.WhenAll(applicationTasks))
                .OrderByDescending(a => a.DateCreated)];
        }

        private Guid? ResolveTemplateId()
        {
            try
            {
                var templateId = HttpContext.Session.GetString("TemplateId");
                if (Guid.TryParse(templateId, out var guid))
                {
                    return guid;
                }

                // Fallback to configuration
                var configuration = HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
                var configured = configuration?["Template:Id"];
                if (Guid.TryParse(configured, out var cfgGuid))
                {
                    // Persist into session for subsequent requests
                    HttpContext.Session.SetString("TemplateId", cfgGuid.ToString());
                    return cfgGuid;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve TemplateId");
            }

            return null;
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