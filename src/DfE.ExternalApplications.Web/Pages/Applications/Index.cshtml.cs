using DfE.ExternalApplications.Application.Options;
using DfE.ExternalApplications.Web.Models.Applications;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Text.Json;
using static DfE.ExternalApplications.Web.Pages.Applications.DashboardModel;

namespace DfE.ExternalApplications.Web.Pages.Applications;

[Authorize(Roles = "Admin, Caseworker")]
public class IndexModel(
    IApplicationsClient applicationsClient,
    IOptions<DashboardOptions> dashboardOptions,
    ILogger<IndexModel> logger) : PageModel
{
    public Guid? TemplateId { get; set; }
    
    public IReadOnlyList<ApplicationWithCalculatedStatus> Applications { get; private set; } = [];

    public int PageSize => dashboardOptions.Value.PageSize;

    public int TotalPages { get; private set; }

    public bool FiltersEnabled => dashboardOptions.Value.EnableApplicationFilters;

    public bool HasError { get; private set; }
    public string? ErrorMessage { get; private set; }

    public bool IsSearchActive => FiltersEnabled && SearchFilters.HasActiveFilters;

    public bool ShowFiltersPanel => IsSearchActive;

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

    public async Task OnGetAsync()
    {
        var templateId = HttpContext.Session.GetString("TemplateId");
        TemplateId = !string.IsNullOrWhiteSpace(templateId) ? Guid.Parse(templateId) : null;
        logger.LogInformation("TemplateId from session: {TemplateId}", TemplateId);
        ValidateSearchFilters();
        await LoadApplicationsAsync();
    }

    private void ValidateSearchFilters()
    {
        if (!FiltersEnabled)
            return;

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

    private async Task LoadApplicationsAsync()
    {
        if (!ModelState.IsValid)
        {
            Applications = Array.Empty<ApplicationWithCalculatedStatus>();
            return;
        }

        if (!TemplateId.HasValue)
        {
            // Try again on next request; show empty state instead of erroring
            logger.LogWarning("TemplateId not available when loading applications; rendering empty dashboard");
            Applications = Array.Empty<ApplicationWithCalculatedStatus>();
            return;
        }

        var pageSize = dashboardOptions.Value.PageSize;
        var filters = FiltersEnabled ? SearchFilters : new DashboardApplicationSearch();
        var result = await applicationsClient.GetApplicationsByTemplateAsync(
            templateId: TemplateId.Value,
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

        Applications = [..(await Task.WhenAll(applicationTasks))
                .OrderByDescending(a => a.DateCreated)];
    }

    // TODO Consider moving this logic to a service class for better separation of concerns and testability, and share with main dashboard.
    /// <summary>
    /// Calculate the actual application status based on response data
    /// </summary>
    public async Task<ApplicationStatus> GetCalculatedApplicationStatusAsync(ApplicationDto application)
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

}
