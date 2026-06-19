using DfE.ExternalApplications.Application.Options;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.Text.Json;
using static DfE.ExternalApplications.Web.Pages.Applications.DashboardModel;

namespace DfE.ExternalApplications.Web.Pages.Applications;

[Authorize(Roles = "Admin, Caseworker")]
public class IndexModel(
    IApplicationsClient applicationsClient,
    IOptions<DashboardOptions> dashboardOptions,
    ILogger<IndexModel> logger) : PageModel
{
    public Guid TemplateId { get; set; } = Guid.Parse("B2F8E7D4-2C46-4A91-8E73-9D5A1F4B6C89"); // HACK SP LSRP
    
    public IReadOnlyList<ApplicationWithCalculatedStatus> Applications { get; private set; } = [];

    public int PageSize => dashboardOptions.Value.PageSize;

    [BindProperty(SupportsGet = true)]
    public int CurrentPage { get; set; } = 1;
    
    public int TotalPages { get; private set; }

    public bool HasError { get; private set; }
    public string? ErrorMessage { get; private set; }

    public bool SearchDone { get; private set; }

    public void OnGet()
    {
        StringValues templateIdValues = Request.Query["templateId"];
        if (templateIdValues.Count != 0)
        {
            string? templateId = templateIdValues.First();
            if (!string.IsNullOrWhiteSpace(templateId))
            {
                TemplateId = Guid.Parse(templateId);
            }
        }
    }

    public async Task OnGetSearchAsync(Guid? templateId)
    {
        if (!templateId.HasValue)
        {
            throw new ArgumentNullException(nameof(templateId)); // TODO SP handle error
        }

        PagedResultOfApplicationDto result = await applicationsClient.GetApplicationsByTemplateAsync(
                templateId: templateId.Value,
                pageNumber: CurrentPage,
                pageSize: PageSize);

        TotalPages = result.TotalPages;
        CurrentPage = Math.Clamp(CurrentPage, 1, Math.Max(1, TotalPages));

        var applicationTasks = result.Items.AsEnumerable().Select(async app => new ApplicationWithCalculatedStatus
        {
            Application = app,
            CalculatedStatus = await GetCalculatedApplicationStatusAsync(app)
        });

        Applications = [.. (await Task.WhenAll(applicationTasks)).OrderByDescending(a => a.DateCreated)];

        SearchDone = true;
    }

    // TODO SP: Consider moving this logic to a service class for better separation of concerns and testability, and share with main dashboard.
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
