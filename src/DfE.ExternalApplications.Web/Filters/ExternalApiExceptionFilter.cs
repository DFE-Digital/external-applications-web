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

                if (r.StatusCode == 400 && r.ExceptionType == "ValidationException"
                    && r.Context?.TryGetValue("validationErrors", out var errsObj) == true)
                {
                    var errors = ((JsonElement)errsObj)
                        .EnumerateObject()
                        .ToDictionary(
                            p => p.Name,
                            p => p.Value.EnumerateArray().Select(e => e.GetString()!).ToArray()
                        );

                    foreach (var kv in errors)
                        foreach (var msg in kv.Value)
                            page.ModelState.AddModelError(kv.Key, msg);

                    executedContext.Result = new PageResult();
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
                    executedContext.Result = new RedirectToPageResult("/Error/Forbidden");
                    executedContext.ExceptionHandled = true;
                    return;
                }

                page.TempData["ApiErrorId"] = r.ErrorId;
                executedContext.Result = new RedirectToPageResult("/Error/General");
                executedContext.ExceptionHandled = true;
            }
        }
    }
}
