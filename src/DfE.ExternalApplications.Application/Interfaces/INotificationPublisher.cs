using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;

namespace DfE.ExternalApplications.Application.Interfaces;

/// <summary>
/// Creates user notifications without failing the calling operation when the API denies access.
/// </summary>
public interface INotificationPublisher
{
    /// <summary>
    /// Attempts to create a notification. Returns <c>false</c> when the user has no notification permission.
    /// Other API failures are rethrown.
    /// </summary>
    /// <param name="request">The notification to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when the notification was created; otherwise <c>false</c>.</returns>
    Task<bool> TryCreateAsync(AddNotificationRequest request, CancellationToken cancellationToken = default);
}
