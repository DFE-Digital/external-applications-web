using DfE.CoreLibs.Http.Models;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace DfE.ExternalApplications.Web.Filters
{
    public class ExternalApiPageExceptionFilter : IAsyncPageFilter
    {
        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)
            => Task.CompletedTask;

        public async Task OnPageHandlerExecutionAsync(
            PageHandlerExecutingContext context,
            PageHandlerExecutionDelegate next)
        {
            var executedContext = await next();

            if (executedContext.Exception is ExternalApplicationsException<ExceptionResponse> ex
                && !executedContext.ExceptionHandled)
            {
                var r = ex.Result;
                var page = context.HandlerInstance as PageModel
                           ?? throw new InvalidOperationException("Page filter only for Razor Pages");

                // 1) Validation: attempt to map structured validation errors into ModelState
                if (r.StatusCode is 400 or 422)
                {
                    if (TryAddModelStateErrorsFromContext(page, r))
                    {
                        executedContext.Result = new PageResult();
                        executedContext.ExceptionHandled = true;
                        return;
                    }
                }

                if (r.StatusCode == 400 || r.StatusCode == 409)
                {
                    AddNonFieldError(page, ex.Result.Message);

                    executedContext.Result = new PageResult();
                    executedContext.ExceptionHandled = true;
                    return;
                }

                if (r.StatusCode == 429)
                {
                    page.TempData["ApiErrorId"] = r.ErrorId;
                    page.TempData["ErrorMessage"] = r.Message;
                    executedContext.Result = new RedirectToPageResult("/Error/General");
                    executedContext.ExceptionHandled = true;
                    return;
                }

                if (r.StatusCode == 401)
                {
                    page.TempData["ApiErrorId"] = r.ErrorId;
                    executedContext.Result = new RedirectToPageResult("/Error/Forbidden");
                    executedContext.ExceptionHandled = true;
                    return;
                }
                if (r.StatusCode == 403)
                {
                    page.TempData["ApiErrorId"] = r.ErrorId;
                    
                    // Check if this is likely a token issue and redirect to logout
                    if (r.Message?.Contains("token", StringComparison.OrdinalIgnoreCase) == true ||
                        r.Message?.Contains("expired", StringComparison.OrdinalIgnoreCase) == true ||
                        r.Message?.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        executedContext.Result = new RedirectToPageResult("/Logout", new { reason = "token_expired" });
                    }
                    else
                    {
                        executedContext.Result = new RedirectToPageResult("/Error/Forbidden");
                    }
                    
                    executedContext.ExceptionHandled = true;
                    return;
                }

                page.TempData["ApiErrorId"] = r.ErrorId;
                executedContext.Result = new RedirectToPageResult("/Error/General");
                executedContext.ExceptionHandled = true;
            }
        }

        private static void AddNonFieldError(PageModel page, string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                page.ModelState.AddModelError("Error", message);
            }
        }

        private static bool TryAddModelStateErrorsFromContext(PageModel page, ExceptionResponse r)
        {
            if (r.Context is null || r.Context.Count == 0)
                return false;

            // Common keys that might hold validation dictionaries
            var possibleKeys = new[] { "validationErrors", "errors", "fieldErrors", "modelState" };
            foreach (var key in possibleKeys)
            {
                if (!r.Context.TryGetValue(key, out var value))
                    continue;

                if (value is JsonElement element)
                {
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in element.EnumerateObject())
                        {
                            // Accept arrays or single string
                            if (prop.Value.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var msg in prop.Value.EnumerateArray().Select(v => v.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)))
                                {
                                    page.ModelState.AddModelError(prop.Name, msg!);
                                }
                            }
                            else if (prop.Value.ValueKind == JsonValueKind.String)
                            {
                                var msg = prop.Value.GetString();
                                if (!string.IsNullOrWhiteSpace(msg))
                                    page.ModelState.AddModelError(prop.Name, msg!);
                            }
                        }
                        return true;
                    }
                }
            }

            // Fallback: add high-level message/details if present
            if (!string.IsNullOrWhiteSpace(r.Message))
            {
                page.ModelState.AddModelError("Error", r.Message);
                return true;
            }

            return false;
        }
    }
}
