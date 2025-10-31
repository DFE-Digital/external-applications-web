using System.Text.Json;
using DfE.ExternalApplications.Application.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Enums;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;
using MassTransit;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DfE.ExternalApplications.Infrastructure.Consumers
{
    /// <summary>
    /// Consumer for file scan results from the virus scanner service.
    /// Listens to the file-scanner-results topic with subscription extweb.
    /// Handles infected files by cleaning them up from Redis sessions and notifying users.
    /// </summary>
    public sealed class ScanResultConsumer(
        IApplicationsClient applicationsClient,
        INotificationsClient notificationsClient,
        IConnectionMultiplexer redis,
        ILogger<ScanResultConsumer> logger) : IConsumer<ScanResultEvent>
    {
        public async Task Consume(ConsumeContext<ScanResultEvent> context)
        {
            var scanResult = context.Message;

            //Thread.Sleep(15000);

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

                // Extract metadata
                if (scanResult.Metadata == null)
                {
                    logger.LogWarning("No Metadata found for infected file {FileId}", fileId);
                    return;
                }

                if (!scanResult.Metadata.ContainsKey("Reference"))
                {
                    logger.LogWarning("No Reference found in Metadata for infected file {FileId}", fileId);
                    return;
                }

                if (!scanResult.Metadata.ContainsKey("userId"))
                {
                    logger.LogWarning("No userId found in Metadata for infected file {FileId}", fileId);
                    return;
                }

                var reference = scanResult.Metadata["Reference"]?.ToString();
                if (string.IsNullOrWhiteSpace(reference))
                {
                    logger.LogWarning("Empty Reference in Metadata for infected file {FileId}", fileId);
                    return;
                }

                var userId = scanResult.Metadata["userId"]?.ToString();
                if (string.IsNullOrWhiteSpace(userId))
                {
                    logger.LogWarning("Empty userId in Metadata for infected file {FileId}", fileId);
                    return;
                }

                // Get applicationId from Metadata (for cache clearing)
                Guid? applicationId = null;
                if (scanResult.Metadata.ContainsKey("applicationId") && 
                    Guid.TryParse(scanResult.Metadata["applicationId"]?.ToString(), out var appId))
                {
                    applicationId = appId;
                }

                logger.LogWarning(
                    "Processing infected file - FileId: {FileId}, FileName: {FileName}, Reference: {Reference}, UserId: {UserId}, MalwareName: {MalwareName}",
                    fileId,
                    scanResult.FileName,
                    reference,
                    userId,
                    scanResult.MalwareName);

                // Use service-to-service authentication for all API calls (database cleanup + notification)
                using (AuthenticationContext.UseServiceToServiceAuthScope())
                {
                    // Clean up infected file from database and clear Redis cache
                    await RemoveInfectedFileFromDatabaseAndCacheAsync(reference, applicationId, fileId, scanResult.FileName, userId);

                    // Create user notification about the infected file
                    await CreateMalwareNotificationAsync(
                        fileId,
                        applicationId ?? Guid.Empty,
                        scanResult.Metadata["originalFileName"].ToString(),
                        scanResult.MalwareName!,
                        new Guid(userId));
                }

                logger.LogInformation(
                "Successfully processed infected file {FileId} ({FileName}) from application {ApplicationId}",
                fileId,
                scanResult.FileName,
                applicationId);
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

        /// <summary>
        /// Removes infected file from database and clears all Redis cache to force fresh data load
        /// </summary>
        private async Task RemoveInfectedFileFromDatabaseAndCacheAsync(string reference, Guid? applicationId, Guid fileId, string fileName, string userId)
        {
            try
            {
                logger.LogInformation(
                    "Cleaning infected file {FileId} from database for application reference {Reference}",
                    fileId,
                    reference);

                // Step 1: Get the application from database using reference
                var application = await applicationsClient.GetApplicationByReferenceAsync(reference);
                if (application == null)
                {
                    logger.LogWarning(
                        "Application with reference {Reference} not found for infected file {FileId}",
                        reference,
                        fileId);
                    return;
                }

                // Step 2: Check if there's response data to clean
                if (application.LatestResponse == null || string.IsNullOrEmpty(application.LatestResponse.ResponseBody))
                {
                    logger.LogInformation(
                        "No response data found for application {Reference}, skipping database cleanup",
                        reference);
                    
                    // Still clear cache and create blacklist even if no database data
                    await ClearRedisCacheForApplicationAsync(application.ApplicationId, fileId, fileName);
                    return;
                }

                string responseJson = application.LatestResponse.ResponseBody;

                // Step 4: Parse and clean the response JSON
                var responseData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);
                if (responseData == null)
                {
                    logger.LogWarning("Failed to deserialize response data for application {Reference}", reference);
                    return;
                }

                bool dataModified = false;

                // Clean each field in the response
                foreach (var (fieldKey, fieldData) in responseData.ToList())
                {
                    if (fieldData.ValueKind != JsonValueKind.Object)
                        continue;

                    // Each field has { "value": "...", "completed": true/false }
                    if (!fieldData.TryGetProperty("value", out var valueElement))
                        continue;

                    if (valueElement.ValueKind != JsonValueKind.String)
                        continue;

                    var valueStr = valueElement.GetString();
                    if (string.IsNullOrEmpty(valueStr))
                        continue;

                    try
                    {
                        // Try to parse as file list
                        var files = JsonSerializer.Deserialize<List<UploadDto>>(valueStr);
                        if (files?.Any(f => f.Id == fileId) == true)
                        {
                            // Remove the infected file
                            files.RemoveAll(f => f.Id == fileId);
                            
                            // Update the field
                            var updatedValueJson = JsonSerializer.Serialize(files);
                            var isCompleted = !string.IsNullOrWhiteSpace(updatedValueJson) && files.Count > 0;
                            
                            responseData[fieldKey] = JsonSerializer.SerializeToElement(new
                            {
                                value = updatedValueJson,
                                completed = isCompleted
                            });

                            dataModified = true;

                            logger.LogInformation(
                                "Removed infected file {FileId} from field {FieldKey} in application {Reference}",
                                fileId,
                                fieldKey,
                                reference);
                        }
                    }
                    catch (JsonException)
                    {
                        // Not a file list, skip
                    }
                }

                // Step 5: If data was modified, save it back to the database
                if (dataModified)
                {
                    var cleanedResponseJson = JsonSerializer.Serialize(responseData);
                    var encodedResponse = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(cleanedResponseJson));

                    var request = new AddApplicationResponseRequest { ResponseBody = encodedResponse };

                    try
                    {
                        await applicationsClient.AddApplicationResponseAsync(application.ApplicationId, request);

                        logger.LogInformation(
                            "Successfully saved cleaned data to database for application {Reference}",
                            reference);
                    }
                    catch (ExternalApplicationsException ex) when (ex.StatusCode == 200)
                    {
                        logger.LogInformation(
                            "Successfully saved cleaned data to database for application {Reference} (200 response)",
                            reference);
                    }
                }
                else
                {
                    logger.LogInformation(
                        "Infected file {FileId} not found in application {Reference} response data",
                        fileId,
                        reference);
                }

                // Step 6: Clear ALL Redis cache keys for this application to force fresh load from cleaned DB
                await ClearRedisCacheForApplicationAsync(application.ApplicationId, fileId, fileName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error cleaning up infected file {FileId} from database for application reference {Reference}",
                    fileId,
                    reference);
                throw;
            }
        }

        /// <summary>
        /// Clears all Redis cache and session keys related to an application to force fresh data load from database.
        /// This includes both cache keys (DfE:Cache:*) and session keys that may contain accumulated form data.
        /// Also creates a blacklist entry for the infected file.
        /// </summary>
        private async Task ClearRedisCacheForApplicationAsync(Guid applicationId, Guid fileId, string fileName)
        {
            try
            {
                var db = redis.GetDatabase();
                var server = redis.GetServer(redis.GetEndPoints().First());

                // Clear cache keys
                var cacheKeys = server.Keys(pattern: $"DfE:Cache:*{applicationId}*").ToList();

                logger.LogInformation(
                    "Found {Count} Redis cache key(s) to clear for application {ApplicationId}",
                    cacheKeys.Count,
                    applicationId);

                foreach (var key in cacheKeys)
                {
                    await db.KeyDeleteAsync(key);
                    logger.LogDebug("Deleted Redis cache key: {Key}", key);
                }

                // Store a marker in Redis indicating this application has been cleaned
                // This marker will be checked when loading session data to force a DB reload
                var cleanedMarkerKey = $"DfE:Cleaned:Application:{applicationId}";
                await db.StringSetAsync(cleanedMarkerKey, DateTimeOffset.UtcNow.ToString("o"), TimeSpan.FromHours(24));
                
                // CRITICAL: Store the infected file ID in a blacklist for 24 hours
                // This ensures the file is filtered out EVERYWHERE it appears, even in cached data
                var infectedFileKey = $"DfE:InfectedFile:{fileId}";
                var infectedFileData = JsonSerializer.Serialize(new
                {
                    FileId = fileId,
                    FileName = fileName,
                    ApplicationId = applicationId,
                    MalwareName = "infected",
                    RemovedAt = DateTimeOffset.UtcNow.ToString("o")
                });
                await db.StringSetAsync(infectedFileKey, infectedFileData, TimeSpan.FromHours(24));
                
                logger.LogInformation(
                    "Set cleaned marker and infected file blacklist for application {ApplicationId} and file {FileId}",
                    applicationId,
                    fileId);

                logger.LogInformation(
                    "Successfully cleared {CacheCount} cache key(s) and set cleaned marker for application {ApplicationId}",
                    cacheKeys.Count,
                    applicationId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error clearing Redis cache and sessions for application {ApplicationId}",
                    applicationId);
                // Don't re-throw - cache clearing failure shouldn't fail the entire process
            }
        }

        /// <summary>
        /// Creates a user notification about the infected file
        /// </summary>
        private async Task CreateMalwareNotificationAsync(
            Guid fileId,
            Guid applicationId,
            string? fileName,
            string malwareName,
            Guid? userId)
        {
            try
            {
                var notification = new AddNotificationRequest
                {
                    Message = $"The selected file '{fileName}' contains a virus called [{malwareName}]. We have deleted the file. Upload a new one.",
                    Category = "malware-detection",
                    Context = $"file-{fileId}",
                    Type = NotificationType.Warning,
                    AutoDismiss = false,
                    Metadata = new Dictionary<string, object>
                    {
                        ["fileId"] = fileId.ToString(),
                        ["fileName"] = fileName,
                        ["malwareName"] = malwareName,
                        ["applicationId"] = applicationId.ToString(),
                        ["detectedAt"] = DateTimeOffset.UtcNow.ToString("o")
                    },
                    UserId = userId
                };

                await notificationsClient.CreateNotificationAsync(notification);

                logger.LogInformation(
                    "Created malware notification for file {FileId} ({FileName})",
                    fileId,
                    fileName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error creating malware notification for file {FileId}",
                    fileId);
                // Don't re-throw - notification failure shouldn't fail the entire process
            }
        }
    }
}
