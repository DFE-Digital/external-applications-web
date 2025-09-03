using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace DfE.ExternalApplications.Web.Filters
{
    /// <summary>
    /// Action filter that intercepts form submissions requiring confirmation
    /// </summary>
    public class ConfirmationInterceptorFilter : IActionFilter, IAsyncPageFilter, IOrderedFilter
    {
        private readonly IButtonConfirmationService _confirmationService;
        private readonly ILogger<ConfirmationInterceptorFilter> _logger;

        public ConfirmationInterceptorFilter(
            IButtonConfirmationService confirmationService,
            ILogger<ConfirmationInterceptorFilter> logger)
        {
            _confirmationService = confirmationService;
            _logger = logger;
        }

        /// <summary>
        /// Execute early in the pipeline to intercept before page handlers
        /// </summary>
        public int Order => -1000;

        /// <summary>
        /// Called before the action method is executed
        /// </summary>
        /// <param name="context">The action executing context</param>
        public void OnActionExecuting(ActionExecutingContext context)
        {
            // Only intercept POST requests to page handlers
            if (context.HttpContext.Request.Method != "POST")
                return;

            // Skip if this is already a confirmed action coming back from confirmation page
            if (context.HttpContext.Request.Query.ContainsKey("confirmed") && 
                context.HttpContext.Request.Query["confirmed"] == "true")
            {
                _logger.LogInformation("Skipping confirmation interception - this is a confirmed action");
                return;
            }

            var request = context.HttpContext.Request;
            var form = request.Form;

            // Check if any button in the form requires confirmation
            var confirmationInfo = FindConfirmationButton(form);
            if (confirmationInfo == null)
                return;

            _logger.LogInformation("Intercepting form submission for confirmation - Handler: {Handler}, DisplayFields: {DisplayFields}",
                confirmationInfo.Handler, string.Join(",", confirmationInfo.DisplayFields));

            // Create confirmation request
            var confirmationRequest = new ConfirmationRequest
            {
                OriginalPagePath = context.HttpContext.Request.Path,
                OriginalHandler = confirmationInfo.Handler,
                OriginalFormData = ExtractFormData(form),
                DisplayFields = confirmationInfo.DisplayFields,
                ReturnUrl = context.HttpContext.Request.GetDisplayUrl()
            };

            // Store confirmation context and redirect
            try
            {
                var token = _confirmationService.CreateConfirmation(confirmationRequest);
                context.Result = new RedirectToPageResult("/Confirmation/Index", new { token });
                
                _logger.LogInformation("Redirecting to confirmation page with token {Token}", token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create confirmation for handler {Handler}", confirmationInfo.Handler);
                // Let the original action proceed if confirmation creation fails
            }
        }

        /// <summary>
        /// Called after the action method is executed (not used)
        /// </summary>
        /// <param name="context">The action executed context</param>
        public void OnActionExecuted(ActionExecutedContext context)
        {
            // Not used for this filter
        }

        // Razor Pages pipeline interception
        public System.Threading.Tasks.Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)
        {
            // Not used
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public async System.Threading.Tasks.Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            // Only intercept POST requests to page handlers
            if (!HttpMethods.IsPost(context.HttpContext.Request.Method))
            {
                await next();
                return;
            }

            // Skip if this is already a confirmed action coming back from confirmation page
            if (context.HttpContext.Request.Query.ContainsKey("confirmed") &&
                context.HttpContext.Request.Query["confirmed"] == "true")
            {
                _logger.LogInformation("Skipping confirmation interception (Razor Pages) - already confirmed");
                await next();
                return;
            }

            var request = context.HttpContext.Request;
            var form = request.HasFormContentType ? await request.ReadFormAsync() : default;

            if (form.Count == 0)
            {
                await next();
                return;
            }

            var confirmationInfo = FindConfirmationButton(form);
            if (confirmationInfo == null)
            {
                await next();
                return;
            }

            _logger.LogInformation("[Pages] Intercepting form submission for confirmation - Handler: {Handler}, DisplayFields: {DisplayFields}",
                confirmationInfo.Handler, string.Join(",", confirmationInfo.DisplayFields));

            var confirmationRequest = new ConfirmationRequest
            {
                OriginalPagePath = context.HttpContext.Request.Path,
                OriginalHandler = confirmationInfo.Handler,
                OriginalFormData = ExtractFormData(form),
                DisplayFields = confirmationInfo.DisplayFields,
                ReturnUrl = context.HttpContext.Request.GetDisplayUrl()
            };

            try
            {
                var token = _confirmationService.CreateConfirmation(confirmationRequest);
                context.Result = new RedirectToPageResult("/Confirmation/Index", new { token });
                _logger.LogInformation("[Pages] Redirecting to confirmation page with token {Token}", token);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Pages] Failed to create confirmation for handler {Handler}", confirmationInfo.Handler);
                await next();
            }
        }

        /// <summary>
        /// Finds confirmation button information in the form data
        /// </summary>
        /// <param name="form">The form collection</param>
        /// <returns>Confirmation button information or null if none found</returns>
        private ConfirmationButtonInfo? FindConfirmationButton(IFormCollection form)
        {
            // Look for the handler that was clicked
            string? clickedHandler = null;
            
            // Check for handler field (standard Razor Pages pattern)
            if (form.ContainsKey("handler") && form["handler"].Count > 0)
            {
                clickedHandler = form["handler"].ToString();
            }

            if (string.IsNullOrEmpty(clickedHandler))
                return null;

            // Check if this handler requires confirmation
            var confirmationCheckKey = $"confirmation-check-{clickedHandler}";
            var displayFieldsKey = $"confirmation-display-fields-{clickedHandler}";

            if (form.ContainsKey(confirmationCheckKey) && form[confirmationCheckKey] == "true")
            {
                var displayFieldsValue = form.ContainsKey(displayFieldsKey) 
                    ? form[displayFieldsKey].ToString() 
                    : string.Empty;

                var displayFields = string.IsNullOrEmpty(displayFieldsValue)
                    ? Array.Empty<string>()
                    : displayFieldsValue.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(f => f.Trim())
                        .ToArray();

                return new ConfirmationButtonInfo
                {
                    Handler = clickedHandler,
                    DisplayFields = displayFields
                };
            }

            return null;
        }

        /// <summary>
        /// Extracts form data into a dictionary
        /// </summary>
        /// <param name="form">The form collection</param>
        /// <returns>Dictionary of form data</returns>
        private Dictionary<string, object> ExtractFormData(IFormCollection form)
        {
            var formData = new Dictionary<string, object>();

            foreach (var key in form.Keys)
            {
                // Skip confirmation-related hidden fields
                if (key.StartsWith("confirmation-"))
                    continue;

                var values = form[key];
                if (values.Count == 1)
                {
                    formData[key] = values[0] ?? string.Empty;
                }
                else if (values.Count > 1)
                {
                    formData[key] = values.ToArray();
                }
                else
                {
                    formData[key] = string.Empty;
                }
            }

            _logger.LogDebug("Extracted {Count} form fields for confirmation", formData.Count);
            return formData;
        }
    }

    /// <summary>
    /// Information about a button that requires confirmation
    /// </summary>
    public class ConfirmationButtonInfo
    {
        /// <summary>
        /// The handler name for the button
        /// </summary>
        public string Handler { get; set; } = string.Empty;

        /// <summary>
        /// The fields to display on the confirmation page
        /// </summary>
        public string[] DisplayFields { get; set; } = Array.Empty<string>();
    }
}
