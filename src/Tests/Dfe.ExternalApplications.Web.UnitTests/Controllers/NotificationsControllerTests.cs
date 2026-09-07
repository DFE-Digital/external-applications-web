using DfE.ExternalApplications.Web.Controllers;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Dfe.ExternalApplications.Web.UnitTests.Controllers;

public class NotificationsControllerTests
{
    [Fact]
    public async Task GetUnreadAsync_Returns403_WithoutThrowing_WhenApiDeniesAccess()
    {
        var client = Substitute.For<INotificationsClient>();
        client.GetUnreadNotificationsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new ExternalApplicationsException<ExceptionResponse>(
                "User does not have permission to read notifications",
                403,
                "{}",
                new Dictionary<string, IEnumerable<string>>(),
                new ExceptionResponse { StatusCode = 403 },
                null));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApplicationName"] = "Transfers" })
            .Build();
        var controller = new NotificationsController(client, configuration);

        var result = await controller.GetUnreadAsync(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, objectResult.StatusCode);
    }
}
