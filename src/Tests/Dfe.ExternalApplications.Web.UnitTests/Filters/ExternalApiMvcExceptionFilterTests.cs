using DfE.ExternalApplications.Web.Filters;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfe.ExternalApplications.Web.UnitTests.Filters;

public class ExternalApiMvcExceptionFilterTests
{
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void OnException_ReturnsStatusCodeResult_NotForbid_WhenApiDeniesAccess(int statusCode)
    {
        var filter = new ExternalApiMvcExceptionFilter(NullLogger<ExternalApiMvcExceptionFilter>.Instance);
        var context = CreateExceptionContext(statusCode, "Forbidden - user does not have required permissions");

        filter.OnException(context);

        Assert.True(context.ExceptionHandled);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(statusCode, result.StatusCode);
        Assert.IsNotType<ForbidResult>(context.Result);
        Assert.IsNotType<UnauthorizedResult>(context.Result);
    }

    [Fact]
    public void OnException_IgnoresNonApiExceptions()
    {
        var filter = new ExternalApiMvcExceptionFilter(NullLogger<ExternalApiMvcExceptionFilter>.Instance);
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ExceptionContext(actionContext, new List<IFilterMetadata>())
        {
            Exception = new InvalidOperationException("not an API error")
        };

        filter.OnException(context);

        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
    }

    private static ExceptionContext CreateExceptionContext(int statusCode, string message)
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var response = new ExceptionResponse
        {
            StatusCode = statusCode,
            ErrorId = "243121",
            ExceptionType = "AuthorizationForbiddenException",
            Message = message
        };

        return new ExceptionContext(actionContext, new List<IFilterMetadata>())
        {
            Exception = new ExternalApplicationsException<ExceptionResponse>(
                message,
                statusCode,
                "{}",
                new Dictionary<string, IEnumerable<string>>(),
                response,
                null)
        };
    }
}
