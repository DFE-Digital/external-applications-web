using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DfE.ExternalApplications.Web.Pages.Applications;

/// <summary>
/// Page model for inviting contributors to an application
/// </summary>
[ExcludeFromCodeCoverage]
[Authorize]
public class ContributorsInviteModel(
    IContributorService contributorService,
    IApplicationStateService applicationStateService,
    IApiErrorParser apiErrorParser,
    IModelStateErrorHandler errorHandler,
    ILogger<ContributorsInviteModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "referenceNumber")]
    public string ReferenceNumber { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Enter the email address of the person you want to invite as a contributor")]
    [EmailAddress(ErrorMessage = "Enter a valid email address")]
    [Display(Name = "Email address")]
    public string EmailAddress { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Enter the name of the person you want to invite as a contributor")]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    public Guid? ApplicationId { get; private set; }
    public bool HasError { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Handles GET request to display the invite form
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
                return RedirectToPage("/Applications/Dashboard");
            }

            ApplicationId = applicationId;
            return Page();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading invite contributor page for application reference {ReferenceNumber}", ReferenceNumber);
            return RedirectToPage("/Applications/Dashboard");
        }
    }

    /// <summary>
    /// Handles POST request to send contributor invitation
    /// </summary>
    public async Task<IActionResult> OnPostSendInviteAsync()
    {
        try
        {
            // Ensure we have a valid application ID
            var (applicationId, _) = await applicationStateService.EnsureApplicationIdAsync(ReferenceNumber, HttpContext.Session);
            
            if (!applicationId.HasValue)
            {
                logger.LogWarning("No application ID found for reference number {ReferenceNumber} when sending invite", ReferenceNumber);
                ModelState.AddModelError("", "Application not found. Please try again.");
                return Page();
            }

            ApplicationId = applicationId;

            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Create invitation request
            var inviteRequest = new InviteContributorRequest
            {
                EmailAddress = EmailAddress,
                Name = Name
            };

            // Send the invitation
            await contributorService.InviteContributorAsync(applicationId.Value, inviteRequest);
            
            logger.LogInformation("Successfully invited contributor {Name} ({Email}) to application {ApplicationId}", 
                Name, EmailAddress, applicationId.Value);

            // Redirect back to contributors page
            return RedirectToPage("/Applications/Contributors", new { referenceNumber = ReferenceNumber });
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error inviting contributor {Email} to application reference {ReferenceNumber}", 
                EmailAddress, ReferenceNumber);
            
            // Try to parse API error response
            var apiErrorResult = apiErrorParser.ParseApiError(ex);
            if (apiErrorResult.IsSuccess && apiErrorResult.ErrorResponse != null)
            {
                errorHandler.AddApiErrorsToModelState(ModelState, apiErrorResult.ErrorResponse);
            }
            else
            {
                ModelState.AddModelError("", "There was a problem sending the invitation. Please try again.");
            }
            
            return Page();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error inviting contributor {Email} to application reference {ReferenceNumber}", 
                EmailAddress, ReferenceNumber);
            ModelState.AddModelError("", "There was a problem sending the invitation. Please try again.");
            return Page();
        }
    }

    /// <summary>
    /// Handles request to cancel and return to contributors page
    /// </summary>
    public IActionResult OnPostCancel()
    {
        logger.LogInformation("User cancelled contributor invitation for application reference {ReferenceNumber}", ReferenceNumber);
        return RedirectToPage("/Applications/Contributors", new { referenceNumber = ReferenceNumber });
    }
} 