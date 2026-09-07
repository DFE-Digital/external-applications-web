using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace DfE.ExternalApplications.Web.Filters
{
    public sealed class ExternalApiMvcExceptionFilter(ILogger<ExternalApiMvcExceptionFilter> logger) : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            if (context.Exception is not ExternalApplicationsException<ExceptionResponse> ex)
            {
                return;
            }

            var r = ex.Result;

            logger.LogWarning("API exception for MVC action. StatusCode: {StatusCode}, ErrorId: {ErrorId}, ExceptionType: {ExceptionType}, Message: {Message}",
                r.StatusCode, r.ErrorId, r.ExceptionType, r.Message);

            var problem = new ProblemDetails
            {
                Title = string.IsNullOrWhiteSpace(r.Message)
                    ? (r.StatusCode is 401 or 403 ? "Access denied" : "API error")
                    : r.Message,
                Status = r.StatusCode,
            };
            problem.Extensions["errorId"] = r.ErrorId;
            problem.Extensions["exceptionType"] = r.ExceptionType;

            // Do not return ForbidResult/UnauthorizedResult. Forbid on the OpenIdConnect scheme
            // recurses until stack overflow and kills the process (aspnet/Security#1376).
            if (r.StatusCode is 401 or 403)
            {
                context.Result = new ObjectResult(problem) { StatusCode = r.StatusCode };
                context.ExceptionHandled = true;
                return;
            }

            if (r.StatusCode is 429)
            {
                context.Result = new ObjectResult(problem) { StatusCode = 429 };
                context.ExceptionHandled = true;
                return;
            }

            if (r.StatusCode is 400 or 409)
            {
                context.Result = new BadRequestObjectResult(problem);
                context.ExceptionHandled = true;
                return;
            }

            context.Result = new ObjectResult(problem) { StatusCode = problem.Status ?? 500 };
            context.ExceptionHandled = true;
        }
    }
}


