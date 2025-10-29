using DfE.ExternalApplications.Application.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DfE.ExternalApplications.Infrastructure.Services;

/// <summary>
/// Service for cleaning up infected or compromised files from application responses and session storage
/// </summary>
public class FileCleanupService(
    IFileUploadService fileUploadService,
    IApplicationResponseService applicationResponseService,
    ILogger<FileCleanupService> logger) : IFileCleanupService
{
    /// <summary>
    /// Removes an infected file from the application response and session storage
    /// </summary>
    public async Task<bool> RemoveInfectedFileAsync(Guid applicationId, Guid fileId, string fileName, ISession session)
    {
        try
        {
            logger.LogWarning("Starting cleanup for infected file {FileId} ({FileName}) in application {ApplicationId}", 
                fileId, fileName, applicationId);

            // Note: Backend has already deleted the file from storage before sending the notification
            // We only need to remove it from session and application response

            // Find and clean up the file from session and application response
            var fieldId = await FindFieldIdForFileAsync(applicationId, fileId, fileName, session);
            
            if (!string.IsNullOrEmpty(fieldId))
            {
                await RemoveFileFromFieldAsync(applicationId, fieldId, fileId, session);
                logger.LogInformation("Successfully cleaned up infected file {FileId} from field {FieldId}", fileId, fieldId);
                return true;
            }

            logger.LogWarning("Could not determine field ID for file {FileId}, attempting session cleanup only", fileId);
            await CleanupSessionFilesAsync(applicationId, fileId, session);
            
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cleaning up infected file {FileId} in application {ApplicationId}", 
                fileId, applicationId);
            return false;
        }
    }

    /// <summary>
    /// Attempts to find which field contains the specified file
    /// </summary>
    private async Task<string?> FindFieldIdForFileAsync(Guid applicationId, Guid fileId, string fileName, ISession session)
    {
        try
        {
            // Get accumulated form data from session
            var accumulatedData = applicationResponseService.GetAccumulatedFormData(session);

            foreach (var kvp in accumulatedData)
            {
                var fieldKey = kvp.Key;
                var fieldValue = kvp.Value?.ToString();

                if (string.IsNullOrWhiteSpace(fieldValue))
                    continue;

                // Try to parse as file list
                try
                {
                    var files = JsonSerializer.Deserialize<List<UploadDto>>(fieldValue);
                    if (files != null && files.Any(f => f.Id == fileId))
                    {
                        logger.LogInformation("Found file {FileId} in field {FieldId}", fileId, fieldKey);
                        return fieldKey;
                    }
                }
                catch (JsonException)
                {
                    // Not a file list, check if it's a collection with nested files
                    try
                    {
                        var items = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(fieldValue);
                        if (items != null)
                        {
                            foreach (var item in items)
                            {
                                foreach (var innerKvp in item)
                                {
                                    var innerValue = innerKvp.Value?.ToString();
                                    if (string.IsNullOrWhiteSpace(innerValue))
                                        continue;

                                    try
                                    {
                                        var innerFiles = JsonSerializer.Deserialize<List<UploadDto>>(innerValue);
                                        if (innerFiles != null && innerFiles.Any(f => f.Id == fileId))
                                        {
                                            logger.LogInformation("Found file {FileId} in collection field {FieldId}, inner key {InnerKey}", 
                                                fileId, fieldKey, innerKvp.Key);
                                            return innerKvp.Key;
                                        }
                                    }
                                    catch (JsonException)
                                    {
                                        // Not a file list, continue
                                    }
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Not a collection, continue
                    }
                }
            }

            // Check session keys for regular (non-collection) file uploads
            foreach (var sessionKey in session.Keys)
            {
                if (sessionKey.StartsWith($"UploadedFiles_{applicationId}_"))
                {
                    var fieldId = sessionKey.Substring($"UploadedFiles_{applicationId}_".Length);
                    var sessionValue = session.GetString(sessionKey);
                    
                    if (!string.IsNullOrWhiteSpace(sessionValue))
                    {
                        try
                        {
                            var files = JsonSerializer.Deserialize<List<UploadDto>>(sessionValue);
                            if (files != null && files.Any(f => f.Id == fileId))
                            {
                                logger.LogInformation("Found file {FileId} in session key {SessionKey}, field {FieldId}", 
                                    fileId, sessionKey, fieldId);
                                return fieldId;
                            }
                        }
                        catch (JsonException)
                        {
                            // Not a valid file list, continue
                        }
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error finding field ID for file {FileId}", fileId);
            return null;
        }
    }

    /// <summary>
    /// Removes the file from the specified field in both session and application response
    /// </summary>
    private async Task RemoveFileFromFieldAsync(Guid applicationId, string fieldId, Guid fileId, ISession session)
    {
        // Get current files for this field
        var currentFiles = await GetFilesForFieldAsync(applicationId, fieldId, session);
        var updatedFiles = currentFiles.Where(f => f.Id != fileId).ToList();

        logger.LogInformation("Removing file {FileId} from field {FieldId}. Before: {BeforeCount}, After: {AfterCount}", 
            fileId, fieldId, currentFiles.Count, updatedFiles.Count);

        // Update session
        UpdateSessionFileList(applicationId, fieldId, updatedFiles, session);

        // Update application response
        await SaveFilesToResponseAsync(applicationId, fieldId, updatedFiles, session);
    }

    /// <summary>
    /// Gets files for a specific field from session or accumulated data
    /// </summary>
    private async Task<List<UploadDto>> GetFilesForFieldAsync(Guid applicationId, string fieldId, ISession session)
    {
        // Check accumulated form data first
        var accumulatedData = applicationResponseService.GetAccumulatedFormData(session);
        if (accumulatedData.TryGetValue(fieldId, out var value))
        {
            var json = value?.ToString();
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var files = JsonSerializer.Deserialize<List<UploadDto>>(json);
                    if (files != null)
                        return files;
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "Failed to deserialize files from accumulated data for field {FieldId}", fieldId);
                }
            }
        }

        // Check session key
        var sessionKey = $"UploadedFiles_{applicationId}_{fieldId}";
        var sessionValue = session.GetString(sessionKey);
        if (!string.IsNullOrWhiteSpace(sessionValue))
        {
            try
            {
                var files = JsonSerializer.Deserialize<List<UploadDto>>(sessionValue);
                if (files != null)
                    return files;
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to deserialize files from session for field {FieldId}", fieldId);
            }
        }

        return new List<UploadDto>();
    }

    /// <summary>
    /// Updates session with the new file list
    /// </summary>
    private void UpdateSessionFileList(Guid applicationId, string fieldId, List<UploadDto> files, ISession session)
    {
        var sessionKey = $"UploadedFiles_{applicationId}_{fieldId}";
        var json = JsonSerializer.Serialize(files);
        session.SetString(sessionKey, json);

        // Also update in accumulated form data
        var accumulatedData = applicationResponseService.GetAccumulatedFormData(session);
        accumulatedData[fieldId] = json;
        
        // Save back to session
        var accumulatedJson = JsonSerializer.Serialize(accumulatedData);
        session.SetString("AccumulatedFormData", accumulatedJson);

        logger.LogInformation("Updated session file list for field {FieldId}, now contains {Count} files", fieldId, files.Count);
    }

    /// <summary>
    /// Saves the updated file list to the application response
    /// </summary>
    private async Task SaveFilesToResponseAsync(Guid applicationId, string fieldId, List<UploadDto> files, ISession session)
    {
        var json = JsonSerializer.Serialize(files);
        var data = new Dictionary<string, object> { { fieldId, json } };
        await applicationResponseService.SaveApplicationResponseAsync(applicationId, data, session);
        
        logger.LogInformation("Saved updated file list to application response for field {FieldId}", fieldId);
    }

    /// <summary>
    /// Cleanup files from all session keys (fallback when field ID is unknown)
    /// </summary>
    private async Task CleanupSessionFilesAsync(Guid applicationId, Guid fileId, ISession session)
    {
        var keysToUpdate = new List<string>();

        // Find all session keys that might contain the file
        foreach (var sessionKey in session.Keys)
        {
            if (sessionKey.StartsWith($"UploadedFiles_{applicationId}_"))
            {
                keysToUpdate.Add(sessionKey);
            }
        }

        foreach (var sessionKey in keysToUpdate)
        {
            var sessionValue = session.GetString(sessionKey);
            if (string.IsNullOrWhiteSpace(sessionValue))
                continue;

            try
            {
                var files = JsonSerializer.Deserialize<List<UploadDto>>(sessionValue);
                if (files != null && files.Any(f => f.Id == fileId))
                {
                    var updatedFiles = files.Where(f => f.Id != fileId).ToList();
                    var updatedJson = JsonSerializer.Serialize(updatedFiles);
                    session.SetString(sessionKey, updatedJson);
                    
                    logger.LogInformation("Removed file {FileId} from session key {SessionKey}", fileId, sessionKey);
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to process session key {SessionKey}", sessionKey);
            }
        }
    }
}

