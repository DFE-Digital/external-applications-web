using DfE.CoreLibs.Http.Models;
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
            Console.WriteLine($"[API FILTER DEBUG] *** FILTER ENTRY - Handler: {context.HandlerMethod?.Name} ***");
            Console.WriteLine($"[API FILTER DEBUG] *** Page: {context.ActionDescriptor.DisplayName} ***");
            
            // DETECT UPLOAD REQUESTS BEFORE EXECUTION
            var uploadInfo = DetectUploadRequest(context);
            if (uploadInfo.isUpload)
            {
                Console.WriteLine($"[API FILTER DEBUG] *** UPLOAD REQUEST DETECTED - FieldId: {uploadInfo.fieldId} ***");
                context.HttpContext.Items["UploadRequestInfo"] = uploadInfo;
            }
            
            var executedContext = await next();

            Console.WriteLine($"[API FILTER DEBUG] *** FILTER POST-EXECUTION ***");
            Console.WriteLine($"[API FILTER DEBUG] *** Exception: {executedContext.Exception?.GetType().Name} ***");
            Console.WriteLine($"[API FILTER DEBUG] *** Exception Handled: {executedContext.ExceptionHandled} ***");

            if (executedContext.Exception is ExternalApplicationsException<ExceptionResponse> ex
                && !executedContext.ExceptionHandled)
            {
                Console.WriteLine($"[API FILTER DEBUG] *** HANDLING ExternalApplicationsException ***");
                Console.WriteLine($"[API FILTER DEBUG] *** Status Code: {ex.Result?.StatusCode} ***");
                Console.WriteLine($"[API FILTER DEBUG] *** Message: {ex.Result?.Message} ***");
                
                var r = ex.Result;
                var page = context.HandlerInstance as PageModel
                           ?? throw new InvalidOperationException("Page filter only for Razor Pages");
                           
                Console.WriteLine($"[API FILTER DEBUG] *** Page Model Type: {page.GetType().Name} ***");

                // 1) Validation: attempt to map structured validation errors into ModelState
                if (r.StatusCode is 400 or 422)
                {
                    Console.WriteLine($"[API FILTER DEBUG] *** 400/422 VALIDATION ERROR PATH ***");
                    if (TryAddModelStateErrorsFromContext(page, r))
                    {
                        Console.WriteLine($"[API FILTER DEBUG] *** Added structured validation errors to ModelState ***");
                        executedContext.Result = new PageResult();
                        executedContext.ExceptionHandled = true;
                        Console.WriteLine($"[API FILTER DEBUG] *** RETURNING PageResult() for validation errors ***");
                        return;
                    }
                }

                if (r.StatusCode == 400 || r.StatusCode == 409)
                {
                    Console.WriteLine($"[API FILTER DEBUG] *** 400/409 CONFLICT ERROR PATH ***");
                    Console.WriteLine($"[API FILTER DEBUG] *** Adding non-field error: {ex.Result.Message} ***");
                    AddNonFieldError(page, ex.Result.Message);

                    // SPECIAL HANDLING FOR UPLOAD REQUESTS: Use stored upload info
                    Console.WriteLine($"[API FILTER DEBUG] *** CHECKING FOR STORED UPLOAD INFO ***");
                    var storedUploadInfo = context.HttpContext.Items.TryGetValue("UploadRequestInfo", out var storedInfo) 
                        ? ((bool isUpload, string fieldId))storedInfo 
                        : (false, string.Empty);
                    Console.WriteLine($"[API FILTER DEBUG] *** STORED UPLOAD INFO: isUpload={storedUploadInfo.Item1}, fieldId='{storedUploadInfo.Item2}' ***");
                    if (storedUploadInfo.Item1)
                    {
                        Console.WriteLine($"[API FILTER DEBUG] *** UPLOAD REQUEST - SAVING TO FORMERRORSTORE ***");
                        try 
                        {
                            var formErrorStore = context.HttpContext.RequestServices.GetService<DfE.ExternalApplications.Web.Interfaces.IFormErrorStore>();
                            if (formErrorStore != null)
                            {
                                formErrorStore.Save(storedUploadInfo.Item2, page.ModelState);
                                Console.WriteLine($"[API FILTER DEBUG] *** SAVED ERRORS TO FORMERRORSTORE FOR FIELD: {storedUploadInfo.Item2} ***");
                                
                                // Get the return URL from the request
                                var returnUrl = context.HttpContext.Request.Form["ReturnUrl"].ToString();
                                if (!string.IsNullOrEmpty(returnUrl))
                                {
                                    Console.WriteLine($"[API FILTER DEBUG] *** REDIRECTING TO RETURN URL: {returnUrl} ***");
                                    executedContext.Result = new Microsoft.AspNetCore.Mvc.RedirectResult(returnUrl);
                                    executedContext.ExceptionHandled = true;
                                    return;
                                }
                            }
                        }
                        catch (Exception saveEx)
                        {
                            Console.WriteLine($"[API FILTER DEBUG] *** ERROR SAVING TO FORMERRORSTORE: {saveEx.Message} ***");
                        }
                    }

                    Console.WriteLine($"[API FILTER DEBUG] *** Setting PageResult() for conflict error ***");
                    executedContext.Result = new PageResult();
                    executedContext.ExceptionHandled = true;
                    Console.WriteLine($"[API FILTER DEBUG] *** EXCEPTION HANDLED - RETURNING PageResult() ***");
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
                    logger?.LogWarning(">>>>>>>>>> Authentication >>> ExternalApiPageExceptionFilter: 401 Unauthorized error for user {UserId} at {Path}. Redirecting to forbidden page.", 
                        userId, context.HttpContext.Request.Path);
                    
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
                        logger?.LogWarning(">>>>>>>>>> Authentication >>> ExternalApiPageExceptionFilter: 403 Forbidden with token-related error for user {UserId} at {Path}. " +
                                          "Error message: {ErrorMessage}. User claims: {UserClaims}. Redirecting to logout.", 
                            userId, context.HttpContext.Request.Path, r.Message, userClaims);
                        
                        executedContext.Result = new RedirectToPageResult("/Logout", new { reason = "token_expired" });
                    }
                    else
                    {
                        logger?.LogWarning(">>>>>>>>>> Authentication >>> ExternalApiPageExceptionFilter: 403 Forbidden error for user {UserId} at {Path}. " +
                                          "User claims: {UserClaims}. Redirecting to forbidden page.", 
                            userId, context.HttpContext.Request.Path, userClaims);
                        
                        executedContext.Result = new RedirectToPageResult("/Error/Forbidden");
                    }
                    
                    executedContext.ExceptionHandled = true;
                    return;
                }

                Console.WriteLine($"[API FILTER DEBUG] *** OTHER STATUS CODE - REDIRECTING TO /Error/General ***");
                page.TempData["ApiErrorId"] = r.ErrorId;
                executedContext.Result = new RedirectToPageResult("/Error/General");
                executedContext.ExceptionHandled = true;
            }
            else
            {
                Console.WriteLine($"[API FILTER DEBUG] *** EXCEPTION NOT HANDLED BY FILTER ***");
                if (executedContext.Exception != null)
                {
                    Console.WriteLine($"[API FILTER DEBUG] *** Exception Type: {executedContext.Exception.GetType().Name} ***");
                    Console.WriteLine($"[API FILTER DEBUG] *** Already Handled: {executedContext.ExceptionHandled} ***");
                }
            }
            
            Console.WriteLine($"[API FILTER DEBUG] *** FILTER EXIT ***");
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
            Console.WriteLine($"[API FILTER DEBUG] *** DETECTING UPLOAD REQUEST - HANDLER: {handlerName} ***");
            
            // Handle both possible handler name formats
            var isUploadHandler = handlerName == "OnPostUploadFileAsync" || handlerName == "UploadFile";
            if (!isUploadHandler)
            {
                Console.WriteLine($"[API FILTER DEBUG] *** NOT UPLOAD HANDLER - EXPECTED: OnPostUploadFileAsync OR UploadFile, GOT: {handlerName} ***");
                return (false, string.Empty);
            }
            
            Console.WriteLine($"[API FILTER DEBUG] *** HANDLER MATCHES - CHECKING FORM DATA ***");
            
            // Try to get FieldId from form data
            if (context.HttpContext.Request.HasFormContentType)
            {
                Console.WriteLine($"[API FILTER DEBUG] *** HAS FORM CONTENT TYPE ***");
                var fieldId = context.HttpContext.Request.Form["FieldId"].ToString();
                Console.WriteLine($"[API FILTER DEBUG] *** FORM FIELDID: '{fieldId}' ***");
                
                if (!string.IsNullOrEmpty(fieldId))
                {
                    Console.WriteLine($"[API FILTER DEBUG] *** UPLOAD REQUEST IDENTIFIED - FieldId: {fieldId} ***");
                    return (true, fieldId);
                }
                else
                {
                    Console.WriteLine($"[API FILTER DEBUG] *** FIELDID IS EMPTY ***");
                }
            }
            else
            {
                Console.WriteLine($"[API FILTER DEBUG] *** NO FORM CONTENT TYPE ***");
            }
            
            Console.WriteLine($"[API FILTER DEBUG] *** UPLOAD REQUEST BUT NO FIELDID FOUND ***");
            return (false, string.Empty);
        }
    }
}
