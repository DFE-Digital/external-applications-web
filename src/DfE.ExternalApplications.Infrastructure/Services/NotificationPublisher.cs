using DfE.ExternalApplications.Application.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.Extensions.Logging;

namespace DfE.ExternalApplications.Infrastructure.Services;

/// <summary>
/// Publishes notifications through the External Applications API and treats 401/403 as a skip.
/// </summary>
public sealed class NotificationPublisher(
    INotificationsClient notificationsClient,
    ILogger<NotificationPublisher> logger) : INotificationPublisher
{
    /// <inheritdoc />
    public async Task<bool> TryCreateAsync(
        AddNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await notificationsClient.CreateNotificationAsync(request, cancellationToken);
            return true;
        }
        catch (ExternalApplicationsException ex) when (ex.StatusCode is 401 or 403)
        {
            logger.LogWarning(
                ex,
                "Skipping notification; user does not have notification permission");
            return false;
        }
    }
}
