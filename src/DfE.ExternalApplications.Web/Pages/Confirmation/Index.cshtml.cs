using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace DfE.ExternalApplications.Web.Pages.Confirmation
{
    /// <summary>
    /// Page model for the confirmation page
    /// </summary>
    public class IndexModel : PageModel
    {
        private readonly IButtonConfirmationService _confirmationService;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(
            IButtonConfirmationService confirmationService,
            ILogger<IndexModel> logger)
        {
            _confirmationService = confirmationService;
            _logger = logger;
        }

        /// <summary>
        /// Whether the user has confirmed the action
        /// </summary>
        [BindProperty]
        public bool Confirmed { get; set; }

        /// <summary>
        /// The confirmation token
        /// </summary>
        [BindProperty]
        public string ConfirmationToken { get; set; } = string.Empty;

        /// <summary>
        /// The display model for the confirmation page
        /// </summary>
        public ConfirmationDisplayModel DisplayModel { get; set; } = new();

        /// <summary>
        /// Handles GET requests to display the confirmation page
        /// </summary>
        /// <param name="token">The confirmation token</param>
        /// <returns>The page result or redirect if invalid</returns>
        public IActionResult OnGet(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Confirmation page accessed without token");
                return RedirectToPage("/Error/General");
            }

            DisplayModel = _confirmationService.PrepareDisplayModel(token);
            if (DisplayModel == null)
            {
                _logger.LogWarning("Invalid or expired confirmation token: {Token}", token);
                return RedirectToPage("/Error/General");
            }

            ConfirmationToken = token;
            _logger.LogInformation("Displaying confirmation page for token {Token}", token);
            
            return Page();
        }

        /// <summary>
        /// Handles POST: reads the user's choice and either executes the original action or returns
        /// </summary>
        public IActionResult OnPost()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var token = ConfirmationToken;
            var context = _confirmationService.GetConfirmation(token);
            if (context == null)
            {
                _logger.LogWarning("Invalid or expired confirmation token on POST: {Token}", token);
                return RedirectToPage("/Error/General");
            }

            // Determine outcome from radio
            var confirmedValue = Request.Form["Confirmed"].ToString();
            var isConfirmed = string.Equals(confirmedValue, "true", StringComparison.OrdinalIgnoreCase);

            if (!isConfirmed)
            {
                _logger.LogInformation("User cancelled confirmation; redirecting to {ReturnUrl}", context.Request.ReturnUrl);
                return Redirect(context.Request.ReturnUrl);
            }

            return ExecuteOriginalAction(context);
        }

        /// <summary>
        /// Executes the original action that was intercepted
        /// </summary>
        /// <param name="context">The confirmation context</param>
        /// <returns>Redirect to execute the original action</returns>
        private IActionResult ExecuteOriginalAction(ConfirmationContext context)
        {
            try
            {
                var originalPath = context.Request.OriginalPagePath;
                var originalHandler = context.Request.OriginalHandler;
                var formData = context.Request.OriginalFormData;

                // Store the confirmed form data in TempData so the target page can access it
                TempData["ConfirmedFormData"] = JsonSerializer.Serialize(formData);
                TempData["ConfirmedHandler"] = originalHandler;

                // Clear the confirmation as it's been used
                _confirmationService.ClearConfirmation(ConfirmationToken);

                // Redirect to the original page with a special flag indicating this is a confirmed action
                var redirectUrl = $"{originalPath}?confirmed=true&handler={originalHandler}";
                
                _logger.LogInformation("Redirecting to execute original action: {RedirectUrl}", redirectUrl);
                return Redirect(redirectUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute original action for confirmation token {Token}", ConfirmationToken);
                return RedirectToPage("/Error/General");
            }
        }
    }
}
