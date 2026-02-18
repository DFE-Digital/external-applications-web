using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Application.Models;

namespace DfE.ExternalApplications.Infrastructure.Services;

/// <summary>
/// No-op submission handler. Use when Handlers list is empty or for tests.
/// </summary>
public class NoOpApplicationSubmittedHandler : IApplicationSubmittedHandler
{
    /// <inheritdoc />
    public Task HandleAsync(ApplicationSubmittedContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
