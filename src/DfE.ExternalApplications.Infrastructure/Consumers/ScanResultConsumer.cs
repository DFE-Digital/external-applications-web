using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DfE.ExternalApplications.Infrastructure.Consumers
{
    /// <summary>
    /// Consumer for file scan results from the virus scanner service.
    /// Listens to the file-scanner-results topic with subscription extweb.
    /// </summary>
    public sealed class ScanResultConsumer(
        ILogger<ScanResultConsumer> logger) : IConsumer<ScanResultEvent>
    {
        public async Task Consume(ConsumeContext<ScanResultEvent> context)
        {
            var scanResult = context.Message;

            logger.LogInformation(
                "Received scan result - FileName: {FileName}, Status: {Status}, Outcome: {Outcome}",
                scanResult.FileName,
                scanResult.Status,
                scanResult.Outcome);
        }
    }
}
