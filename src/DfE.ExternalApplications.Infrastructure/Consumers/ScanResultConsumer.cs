using DfE.ExternalApplications.Application.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Enums;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DfE.ExternalApplications.Infrastructure.Consumers
{
    /// <summary>
    /// Consumer for file scan results from the virus scanner service.
    /// Listens to the file-scanner-results topic with subscription extweb.
    /// Handles infected files by cleaning them up and notifying users.
    /// </summary>
    public sealed class ScanResultConsumer(
        IFileCleanupService fileCleanupService,
        INotificationsClient notificationsClient,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ScanResultConsumer> logger) : IConsumer<ScanResultEvent>
    {
        public async Task Consume(ConsumeContext<ScanResultEvent> context)
        {
            var scanResult = context.Message;

            logger.LogInformation(
                "Received scan result - FileName: {FileName}, FileId: {FileId}, Status: {Status}, Outcome: {Outcome}, MalwareName: {MalwareName}",
                scanResult.FileName,
                scanResult.FileId,
                scanResult.Status,
                scanResult.Outcome,
                scanResult.MalwareName);

            // Check if the file is infected
            if (IsInfected(scanResult))
            {
                await HandleInfectedFileAsync(scanResult);
            }
            else if (scanResult.Outcome == VirusScanOutcome.Clean)
            {
                logger.LogInformation(
                    "File {FileName} ({FileId}) is clean",
                    scanResult.FileName,
                    scanResult.FileId);
            }
            else
            {
                logger.LogWarning(
                    "Scan completed with unexpected outcome - FileName: {FileName}, FileId: {FileId}, Outcome: {Outcome}",
                    scanResult.FileName,
                    scanResult.FileId,
                    scanResult.Outcome);
            }
        }

        /// <summary>
        /// Checks if the scan result indicates an infected file
        /// </summary>
        private bool IsInfected(ScanResultEvent scanResult)
        {
            return scanResult.Outcome == VirusScanOutcome.Infected
                   && !string.IsNullOrWhiteSpace(scanResult.MalwareName);
        }

        /// <summary>
        /// Handles an infected file by cleaning it up and notifying the user
        /// </summary>
        private async Task HandleInfectedFileAsync(ScanResultEvent scanResult)
        {
            try
            {
                if (!Guid.TryParse(scanResult.FileId, out var fileId))
                {
                    logger.LogWarning(
                        "Invalid FileId in scan result: {FileId}",
                        scanResult.FileId);
                    return;
                }

                // Extract applicationId from Reference (format: "applicationId:fieldId")
                if (string.IsNullOrWhiteSpace(scanResult.Reference))
                {
                    logger.LogWarning(
                        "No Reference found in scan result for infected file {FileId}",
                        fileId);
                    return;
                }

                var referenceParts = scanResult.Reference.Split(':');
                if (referenceParts.Length < 1 || !Guid.TryParse(referenceParts[0], out var applicationId))
                {
                    logger.LogWarning(
                        "Could not parse ApplicationId from Reference: {Reference}",
                        scanResult.Reference);
                    return;
                }

                logger.LogWarning(
                    "Processing infected file - FileId: {FileId}, FileName: {FileName}, ApplicationId: {ApplicationId}, MalwareName: {MalwareName}",
                    fileId,
                    scanResult.FileName,
                    applicationId,
                    scanResult.MalwareName);

                // Get session from HTTP context (if available)
                var session = httpContextAccessor.HttpContext?.Session;

                // Clean up the infected file
                bool cleanupSuccess = false;
                if (session != null)
                {
                    cleanupSuccess = await fileCleanupService.RemoveInfectedFileAsync(
                        applicationId,
                        fileId,
                        scanResult.FileName,
                        session);
                }
                else
                {
                    logger.LogWarning(
                        "No active session available for file cleanup. File {FileId} will be cleaned on next user request.",
                        fileId);
                }

                // Create user notification about the infected file
                //await CreateMalwareNotificationAsync(
                //    fileId,
                //    applicationId,
                //    scanResult.FileName,
                //    scanResult.MalwareName!);

                if (cleanupSuccess)
                {
                    logger.LogInformation(
                        "Successfully processed infected file {FileId} ({FileName}) from application {ApplicationId}",
                        fileId,
                        scanResult.FileName,
                        applicationId);
                }
                else
                {
                    logger.LogWarning(
                        "Infected file {FileId} notification created, but cleanup will occur on next user request",
                        fileId);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error handling infected file - FileName: {FileName}, FileId: {FileId}",
                    scanResult.FileName,
                    scanResult.FileId);
                
                // Re-throw to let MassTransit handle retry logic
                throw;
            }
        }

        ///// <summary>
        ///// Creates a user notification about the infected file
        ///// </summary>
        //private async Task CreateMalwareNotificationAsync(
        //    Guid fileId,
        //    Guid applicationId,
        //    string fileName,
        //    string malwareName)
        //{
        //    try
        //    {
        //        var notification = new AddNotificationRequest
        //        {
        //            Message = $"The file '{fileName}' was infected with malware ({malwareName}) and has been removed.",
        //            Category = "malware-detection",
        //            Context = $"file-{fileId}",
        //            Type = NotificationType.Warning,
        //            AutoDismiss = false,
        //            Metadata = new Dictionary<string, string>
        //            {
        //                ["fileId"] = fileId.ToString(),
        //                ["fileName"] = fileName,
        //                ["malwareName"] = malwareName,
        //                ["applicationId"] = applicationId.ToString(),
        //                ["detectedAt"] = DateTimeOffset.UtcNow.ToString("o")
        //            }
        //        };

        //        await notificationsClient.CreateNotificationAsync(notification);

        //        logger.LogInformation(
        //            "Created malware notification for file {FileId} ({FileName})",
        //            fileId,
        //            fileName);
        //    }
        //    catch (Exception ex)
        //    {
        //        logger.LogError(ex,
        //            "Error creating malware notification for file {FileId}",
        //            fileId);
        //        // Don't re-throw - notification failure shouldn't fail the entire process
        //    }
        //}
    }
}
