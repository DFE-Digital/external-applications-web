using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Web.Pages.Shared;
using DfE.ExternalApplications.Web.Services;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Web.Pages
{
    [ExcludeFromCodeCoverage]
    public class ApplicationPreviewModel(
        IFieldRendererService renderer,
        IApplicationResponseService applicationResponseService,
        IFieldFormattingService fieldFormattingService,
        ITemplateManagementService templateManagementService,
        IApplicationStateService applicationStateService,
        IApplicationsClient applicationsClient,
        ILogger<ApplicationPreviewModel> logger)
        : BaseFormPageModel(renderer, applicationResponseService, fieldFormattingService, templateManagementService,
            applicationStateService, logger)
    {
        public async Task<IActionResult> OnGetAsync()
        {
            await CommonInitializationAsync();

            // If application is not in progress, still allow viewing but with restrictions
            if (!IsApplicationEditable())
            {
                // Allow viewing but change links will be hidden
                return Page();
            }

            // Check if all tasks are completed before allowing access (for InProgress applications)
            if (!AreAllTasksCompleted())
            {
                return RedirectToPage("/RenderForm", new { referenceNumber = ReferenceNumber });
            }

            return Page();
        }

        public async Task<IActionResult> OnPostSubmitAsync()
        {
            await CommonInitializationAsync();

            // Check if all tasks are completed before allowing submission
            if (!AreAllTasksCompleted())
            {
                _logger.LogWarning("Cannot submit application {ReferenceNumber} - not all tasks completed", ReferenceNumber);
                ModelState.AddModelError("", "All sections must be completed before you can submit your application.");
                return Page();
            }

            if (!ApplicationId.HasValue)
            {
                _logger.LogError("ApplicationId not found during submission for reference {ReferenceNumber}", ReferenceNumber);
                ModelState.AddModelError("", "Application not found. Please try again.");
                return Page();
            }

            try
            {
                _logger.LogInformation("Attempting to submit application {ApplicationId} with reference {ReferenceNumber}", 
                    ApplicationId.Value, ReferenceNumber);

                // Submit the application via API
                var submittedApplication = await applicationsClient.SubmitApplicationAsync(ApplicationId.Value);
                
                // Update session with new application status
                if (submittedApplication != null)
                {
                    var statusKey = $"ApplicationStatus_{ApplicationId.Value}";
                    HttpContext.Session.SetString(statusKey, submittedApplication.Status?.ToString() ?? "Submitted");
                    _logger.LogInformation("Successfully submitted application {ApplicationId} with reference {ReferenceNumber}", 
                        ApplicationId.Value, ReferenceNumber);
                }
                else
                {
                    _logger.LogWarning("Submit API returned null for application {ApplicationId}", ApplicationId.Value);
                }
                
                return RedirectToPage("/ApplicationSubmitted", new { referenceNumber = ReferenceNumber });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to submit application {ApplicationId} with reference {ReferenceNumber}", 
                    ApplicationId.Value, ReferenceNumber);
                
                ModelState.AddModelError("", $"An error occurred while submitting your application: {ex.Message}. Please try again.");
                return Page();
            }
        }
    }
} 