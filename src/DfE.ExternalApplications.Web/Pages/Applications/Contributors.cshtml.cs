using System.Diagnostics.CodeAnalysis;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace DfE.ExternalApplications.Web.Pages.Applications;

/// <summary>
/// Page model for managing application contributors
/// </summary>
[ExcludeFromCodeCoverage]
[Authorize]
public class ContributorsModel(
    IContributorService contributorService,
    IApplicationStateService applicationStateService,
    ILogger<ContributorsModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "referenceNumber")]
    public string ReferenceNumber { get; set; } = string.Empty;

    public Guid? ApplicationId { get; private set; }
    public IReadOnlyList<UserDto> Contributors { get; private set; } = Array.Empty<UserDto>();
    public bool HasError { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Handles GET request to display contributors
    /// </summary>
    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            // Ensure we have a valid application ID
            var (applicationId, _) = await applicationStateService.EnsureApplicationIdAsync(ReferenceNumber, HttpContext.Session);
            
            if (!applicationId.HasValue)
            {
                logger.LogWarning("No application ID found for reference number {ReferenceNumber}", ReferenceNumber);
                HasError = true;
                ErrorMessage = "Application not found. Please try again.";
                return Page();
            }

            ApplicationId = applicationId;

            // Load contributors for the application
            Contributors = await contributorService.GetApplicationContributorsAsync(applicationId.Value);
            
            logger.LogInformation("Loaded {Count} contributors for application {ApplicationId}", 
                Contributors.Count, applicationId.Value);

            return Page();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading contributors for application reference {ReferenceNumber}", ReferenceNumber);
            HasError = true;
            ErrorMessage = "There was a problem loading contributors. Please try again later.";
            return Page();
        }
    }

    /// <summary>
    /// Handles request to proceed to the application form
    /// </summary>
    public IActionResult OnPostProceedToForm()
    {
        logger.LogInformation("User proceeding to form for application reference {ReferenceNumber}", ReferenceNumber);
        return RedirectToPage("/FormEngine/RenderForm", new { referenceNumber = ReferenceNumber });
    }

    /// <summary>
    /// Handles request to add a contributor
    /// </summary>
    public IActionResult OnPostAddContributor()
    {
        logger.LogInformation("User navigating to add contributor for application reference {ReferenceNumber}", ReferenceNumber);
        return RedirectToPage("/Applications/Contributors-Invite", new { referenceNumber = ReferenceNumber });
    }

    /// <summary>
    /// Handles request to remove a contributor
    /// </summary>
    public async Task<IActionResult> OnPostRemoveContributorAsync(Guid contributorId)
    {
        try
        {
            if (!ApplicationId.HasValue)
            {
                var (applicationId, _) = await applicationStateService.EnsureApplicationIdAsync(ReferenceNumber, HttpContext.Session);
                ApplicationId = applicationId;
            }

            if (!ApplicationId.HasValue)
            {
                logger.LogWarning("No application ID found for reference number {ReferenceNumber} when removing contributor", ReferenceNumber);
                HasError = true;
                ErrorMessage = "Application not found. Please try again.";
                return await OnGetAsync();
            }

            await contributorService.RemoveContributorAsync(ApplicationId.Value, contributorId);
            
            logger.LogInformation("Successfully removed contributor {ContributorId} from application {ApplicationId}", 
                contributorId, ApplicationId.Value);

            // Redirect back to contributors page to refresh the list
            return RedirectToPage("/Applications/Contributors", new { referenceNumber = ReferenceNumber });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing contributor {ContributorId} from application reference {ReferenceNumber}", 
                contributorId, ReferenceNumber);
            HasError = true;
            ErrorMessage = "There was a problem removing the contributor. Please try again.";
            return await OnGetAsync();
        }
    }

    /// <summary>
    /// Handles confirmed removal coming back from the confirmation page via GET
    /// </summary>
    public async Task<IActionResult> OnGetRemoveContributorAsync()
    {
        // Only proceed if this is a confirmed action
        if (!Request.Query.ContainsKey("confirmed") || Request.Query["confirmed"] != "true")
        {
            return await OnGetAsync();
        }

        try
        {
            // Restore confirmed form data from TempData
            var confirmedDataJson = TempData["ConfirmedFormData"]?.ToString();
            if (string.IsNullOrEmpty(confirmedDataJson))
            {
                return await OnGetAsync();
            }

            Guid contributorId = Guid.Empty;

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(confirmedDataJson);
                if (dict != null && dict.TryGetValue("contributorId", out var je))
                {
                    var idStr = je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
                    if (!string.IsNullOrWhiteSpace(idStr))
                    {
                        Guid.TryParse(idStr, out contributorId);
                    }
                }
            }
            catch
            {
                // Ignore and fall back to empty id
            }

            if (contributorId == Guid.Empty)
            {
                // Unable to determine contributor, just reload
                return await OnGetAsync();
            }

            if (!ApplicationId.HasValue)
            {
                var (applicationId, _) = await applicationStateService.EnsureApplicationIdAsync(ReferenceNumber, HttpContext.Session);
                ApplicationId = applicationId;
            }

            if (!ApplicationId.HasValue)
            {
                logger.LogWarning("No application ID found for reference number {ReferenceNumber} when confirming removal", ReferenceNumber);
                HasError = true;
                ErrorMessage = "Application not found. Please try again.";
                return await OnGetAsync();
            }

            await contributorService.RemoveContributorAsync(ApplicationId.Value, contributorId);

            logger.LogInformation("Successfully removed contributor {ContributorId} from application {ApplicationId} via confirmation", 
                contributorId, ApplicationId.Value);

            return RedirectToPage("/Applications/Contributors", new { referenceNumber = ReferenceNumber });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing contributor via confirmed GET for application reference {ReferenceNumber}", ReferenceNumber);
            HasError = true;
            ErrorMessage = "There was a problem removing the contributor. Please try again.";
            return await OnGetAsync();
        }
    }
} 