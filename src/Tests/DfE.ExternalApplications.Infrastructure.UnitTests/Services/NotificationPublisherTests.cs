using DfE.ExternalApplications.Infrastructure.Services;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DfE.ExternalApplications.Infrastructure.UnitTests.Services;

public class NotificationPublisherTests
{
    [Fact]
    public async Task TryCreateAsync_ReturnsTrue_WhenApiSucceeds()
    {
        var client = Substitute.For<INotificationsClient>();
        var publisher = new NotificationPublisher(client, NullLogger<NotificationPublisher>.Instance);
        var request = new AddNotificationRequest { Message = "ok" };

        var created = await publisher.TryCreateAsync(request);

        Assert.True(created);
        await client.Received(1).CreateNotificationAsync(request, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task TryCreateAsync_ReturnsFalse_WhenApiDeniesAccess(int statusCode)
    {
        var client = Substitute.For<INotificationsClient>();
        client.CreateNotificationAsync(Arg.Any<AddNotificationRequest>(), Arg.Any<CancellationToken>())
            .Throws(new ExternalApplicationsException<ExceptionResponse>(
                "Forbidden - user does not have required permissions",
                statusCode,
                "{}",
                new Dictionary<string, IEnumerable<string>>(),
                new ExceptionResponse { StatusCode = statusCode },
                null));

        var publisher = new NotificationPublisher(client, NullLogger<NotificationPublisher>.Instance);

        var created = await publisher.TryCreateAsync(new AddNotificationRequest { Message = "skip" });

        Assert.False(created);
    }
}
