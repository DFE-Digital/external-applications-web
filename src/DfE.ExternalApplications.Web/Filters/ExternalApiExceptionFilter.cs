using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using DfE.ExternalApplications.Web.Interfaces;

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
            
            // DETECT UPLOAD REQUESTS BEFORE EXECUTION
            var uploadInfo = DetectUploadRequest(context);
            if (uploadInfo.isUpload)
            {
                
                context.HttpContext.Items["UploadRequestInfo"] = uploadInfo;
            }
            
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

                    // SPECIAL HANDLING FOR UPLOAD REQUESTS: Use stored upload info
                    
                    var storedUploadInfo = context.HttpContext.Items.TryGetValue("UploadRequestInfo", out var storedInfo) 
                        ? ((bool isUpload, string fieldId))storedInfo 
                        : (false, string.Empty);
                    
                    if (storedUploadInfo.Item1)
                    {
                        
                        try 
                        {
                            var formErrorStore = context.HttpContext.RequestServices.GetService<DfE.ExternalApplications.Web.Interfaces.IFormErrorStore>();
                            if (formErrorStore != null)
                            {
                                formErrorStore.Save(storedUploadInfo.Item2, page.ModelState);
                                
                                // Get the return URL from the request
                                var returnUrl = context.HttpContext.Request.Form["ReturnUrl"].ToString();
                                if (!string.IsNullOrEmpty(returnUrl))
                                {
                                    
                                    executedContext.Result = new Microsoft.AspNetCore.Mvc.RedirectResult(returnUrl);
                                    executedContext.ExceptionHandled = true;
                                    return;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            
                        }
                    }

                    
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
                    var logger = context.HttpContext.RequestServices.GetService<ILogger<ExternalApiPageExceptionFilter>>();
                    var userId = context.HttpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "Anonymous";

                    
                    page.TempData["ApiErrorId"] = r.ErrorId;
                    executedContext.Result = new RedirectToPageResult("/Error/Forbidden");
                    executedContext.ExceptionHandled = true;
                    return;
                }
                if (r.StatusCode == 403)
                {
                    var logger = context.HttpContext.RequestServices.GetService<ILogger<ExternalApiPageExceptionFilter>>();
                    var userId = context.HttpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "Anonymous";
                    var userClaims = string.Join(", ", context.HttpContext.User?.Claims?.Select(c => $"{c.Type}:{c.Value}") ?? Array.Empty<string>());
                    
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
        
        private static (bool isUpload, string fieldId) DetectUploadRequest(PageHandlerExecutingContext context)
        {
            // Check if this is an upload handler
            var handlerName = context.HandlerMethod?.Name;
            
            // Handle both possible handler name formats
            var isUploadHandler = handlerName == "OnPostUploadFileAsync" || handlerName == "UploadFile";
            if (!isUploadHandler)
            {
                
                return (false, string.Empty);
            }
            
            
            
            // Try to get FieldId from form data
            if (context.HttpContext.Request.HasFormContentType)
            {
                var fieldId = context.HttpContext.Request.Form["FieldId"].ToString();
                if (!string.IsNullOrEmpty(fieldId))
                {
                    return (true, fieldId);
                }
            }
            
            return (false, string.Empty);
        }
    }
}
