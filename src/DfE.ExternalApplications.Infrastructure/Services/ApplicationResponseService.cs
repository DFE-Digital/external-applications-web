using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using DfE.ExternalApplications.Application.Interfaces;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Infrastructure.Services;

public class ApplicationResponseService(
    IApplicationsClient applicationsClient,
    INotificationsClient notificationsClient,
    ILogger<ApplicationResponseService> logger)
    : IApplicationResponseService
{
    private const string SessionKeyFormData = "AccumulatedFormData";
    private const string ProcessedFilesSessionKey = "ProcessedMalwareFiles";

    public async Task SaveApplicationResponseAsync(Guid applicationId, Dictionary<string, object> formData, ISession session, CancellationToken cancellationToken = default)
    {
        try
        {
            // CRITICAL: Check for malware notifications and filter out infected files BEFORE saving
            formData = await FilterInfectedFilesAsync(applicationId, formData, session, cancellationToken);
            
            // Accumulate the new data with existing data
            AccumulateFormData(formData, session);
            
            // Get all accumulated data
            var allFormData = GetAccumulatedFormData(session);
            
            var taskStatusData = GetTaskStatusFromSession(applicationId, session);
            
            var responseJson = TransformToResponseJson(allFormData, taskStatusData);
            
            var encodedResponse = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(responseJson));

            var request = new AddApplicationResponseRequest { ResponseBody = encodedResponse };
            await applicationsClient.AddApplicationResponseAsync(applicationId, request, cancellationToken);
            
            // Update application status to InProgress when any data is saved
            // This ensures the dashboard shows the correct status
            await EnsureApplicationStatusIsInProgress(applicationId, allFormData, session, cancellationToken);
            
            logger.LogInformation("Successfully saved application response for {ApplicationId}", applicationId);
        }
        catch (ExternalApplicationsException ex) when (ex.StatusCode == 200)
        {
            // Handle the case where API returns 200 instead of 201
            logger.LogInformation("Application response saved successfully with status 200 for {ApplicationId}", applicationId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save application response for {ApplicationId}", applicationId);
            throw;
        }
    }

    private async Task EnsureApplicationStatusIsInProgress(Guid applicationId, Dictionary<string, object> allFormData, ISession session, CancellationToken cancellationToken)
    {
        try
        {
            // Check if any form fields have data
            var hasAnyData = allFormData.Any(kvp => !string.IsNullOrWhiteSpace(kvp.Value?.ToString()));
            
            if (hasAnyData)
            {
                // Get current application status from session
                var statusKey = $"ApplicationStatus_{applicationId}";
                var currentStatus = session.GetString(statusKey);
                
                // Only update if not already submitted
                if (string.IsNullOrEmpty(currentStatus) || currentStatus.Equals("InProgress", StringComparison.OrdinalIgnoreCase))
                {
                    // Update session status to InProgress
                    session.SetString(statusKey, "InProgress");
                    logger.LogInformation("Updated application {ApplicationId} status to InProgress due to form data being saved", applicationId);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update application status for {ApplicationId}, continuing with save operation", applicationId);
            // Don't throw - this is not critical to the save operation
        }
    }

    public void AccumulateFormData(Dictionary<string, object> newData, ISession session)
    {
        var existingData = GetAccumulatedFormData(session);
        
        foreach (var kvp in newData)
        {
            var normalizedFieldName = NormalizeFieldName(kvp.Key);
            var fieldNameToUse = normalizedFieldName;
            
            logger.LogDebug("Normalizing field name: '{OriginalKey}' -> '{NormalizedKey}'", kvp.Key, fieldNameToUse);
            
            existingData[fieldNameToUse] = kvp.Value;
            
            var alternativeKeys = existingData.Keys
                .Where(key => key != fieldNameToUse && AreEquivalentFieldNames(key, fieldNameToUse))
                .ToList();
            
            foreach (var altKey in alternativeKeys)
            {
                logger.LogDebug("Removing duplicate field entry: {OldKey} in favor of {NewKey}", altKey, fieldNameToUse);
                existingData.Remove(altKey);
            }
        }
        
        var jsonString = JsonSerializer.Serialize(existingData);
        session.SetString(SessionKeyFormData, jsonString);
    }

    private bool AreEquivalentFieldNames(string fieldName1, string fieldName2)
    {
        var normalized1 = NormalizeFieldName(fieldName1);
        var normalized2 = NormalizeFieldName(fieldName2);
        
        return string.Equals(normalized1, normalized2, StringComparison.OrdinalIgnoreCase);
    }

    private string NormalizeFieldName(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
            return fieldName;
            
        if (fieldName.StartsWith("Data_", StringComparison.OrdinalIgnoreCase))
        {
            return fieldName.Substring(5);
        }
        
        return fieldName;
    }

    public Dictionary<string, object> GetAccumulatedFormData(ISession session)
    {
        var jsonString = session.GetString(SessionKeyFormData);
        
        if (string.IsNullOrEmpty(jsonString))
        {
            return new Dictionary<string, object>();
        }
        
        try
        {
            var rawData = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString) 
                         ?? new Dictionary<string, object>();
            
            var cleanedData = new Dictionary<string, object>();
            foreach (var kvp in rawData)
            {
                cleanedData[kvp.Key] = CleanFormValue(kvp.Value);
            }
            
            return cleanedData;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize accumulated form data from session. Starting fresh.");
            return new Dictionary<string, object>();
        }
    }
    
    private object CleanFormValue(object value)
    {
        if (value == null)
            return string.Empty;

        if (value is JsonElement jsonElement)
        {
            switch (jsonElement.ValueKind)
            {
                case JsonValueKind.String:
                    return jsonElement.GetString() ?? string.Empty;
                case JsonValueKind.Array:
                    var allStrings = jsonElement.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String);
                    if (allStrings)
                    {
                        return jsonElement.GetArrayLength() == 1
                            ? jsonElement[0].GetString() ?? string.Empty
                            : jsonElement.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray();
                    }
                    // return raw JSON for arrays of objects
                    return jsonElement.ToString();
                case JsonValueKind.Number:
                    return jsonElement.GetDecimal().ToString();
                case JsonValueKind.True:
                    return "true";
                case JsonValueKind.False:
                    return "false";
                case JsonValueKind.Object:
                    return jsonElement.ToString();
                default:
                    return value.ToString() ?? string.Empty;
            }
        }

        if (value is string[] stringArray && stringArray.Length == 1)
        {
            return stringArray[0];
        }
        
        return value.ToString() ?? string.Empty;
    }

    public void ClearAccumulatedFormData(ISession session)
    {
        session.Remove(SessionKeyFormData);
        logger.LogInformation("Cleared accumulated form data from session");
    }

    public string TransformToResponseJson(Dictionary<string, object> formData, Dictionary<string, string> taskStatusData)
    {
        var responseData = new Dictionary<string, object>();

        // Add form field data
        foreach (var kvp in formData)
        {
            var fieldId = kvp.Key;
            var value = kvp.Value?.ToString() ?? string.Empty;
            
            // Check if the field has a value (completed)
            var isCompleted = !string.IsNullOrWhiteSpace(value);

            responseData[fieldId] = new
            {
                value = value,
                completed = isCompleted
            };
        }

        // Add task completion status
        foreach (var kvp in taskStatusData)
        {
            var taskStatusKey = $"TaskStatus_{kvp.Key}";
            responseData[taskStatusKey] = new
            {
                value = kvp.Value,
                completed = true
            };
        }

        return JsonSerializer.Serialize(responseData, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    public Dictionary<string, string> GetTaskStatusFromSession(Guid applicationId, ISession session)
    {
        var taskStatusData = new Dictionary<string, string>();
        
        var sessionKeys = session.Keys.Where(k => k.StartsWith($"TaskStatus_{applicationId}_")).ToList();
        
        foreach (var sessionKey in sessionKeys)
        {
            var taskId = sessionKey.Substring($"TaskStatus_{applicationId}_".Length);
            var statusValue = session.GetString(sessionKey);
            
            if (!string.IsNullOrEmpty(statusValue))
            {
                taskStatusData[taskId] = statusValue;
            }
        }
        
        return taskStatusData;
    }

    public void SaveTaskStatusToSession(Guid applicationId, string taskId, string status, ISession session)
    {
        var sessionKey = $"TaskStatus_{applicationId}_{taskId}";
        session.SetString(sessionKey, status);
    }

    public void StoreFormDataInSession(Dictionary<string, object> formData, ISession session)
    {
        // Clear existing data and store new data
        ClearAccumulatedFormData(session);
        AccumulateFormData(formData, session);
    }

    public void SetCurrentAccumulatedApplicationId(Guid applicationId, ISession session)
    {
        session.SetString("CurrentAccumulatedApplicationId", applicationId.ToString());
    }

    /// <summary>
    /// Filters out infected files from form data before saving
    /// </summary>
    private async Task<Dictionary<string, object>> FilterInfectedFilesAsync(
        Guid applicationId, 
        Dictionary<string, object> formData,
        ISession session,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get all notifications to check for malware warnings
            var notifications = await notificationsClient.GetAllNotificationsAsync(cancellationToken);
            
            if (notifications == null || notifications.Count == 0)
                return formData;

            // Get already processed files from session to avoid duplicate processing
            var processedFiles = GetProcessedFilesFromSession(session);

            // Find malware notifications for this application
            var malwareNotifications = notifications
                .Where(n => IsMalwareNotification(n, applicationId))
                .Where(n =>
                {
                    // Skip already processed files
                    var fileIdStr = n.Metadata?["fileId"]?.ToString();
                    if (!string.IsNullOrEmpty(fileIdStr) && processedFiles.Contains(fileIdStr))
                    {
                        logger.LogDebug("Skipping already processed malware file in FilterInfectedFilesAsync: FileId={FileId}", fileIdStr);
                        return false;
                    }
                    return true;
                })
                .ToList();

            if (malwareNotifications.Count == 0)
                return formData;

            logger.LogWarning("Found {Count} unprocessed malware notifications for application {ApplicationId}, filtering infected files",
                malwareNotifications.Count, applicationId);

            // Get list of infected file IDs
            var infectedFileIds = malwareNotifications
                .Select(n => n.Metadata?["fileId"]?.ToString())
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => Guid.TryParse(id, out var guid) ? guid : (Guid?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            if (infectedFileIds.Count == 0)
                return formData;

            logger.LogWarning("Filtering {Count} infected file(s) from form data", infectedFileIds.Count);
            
            // Mark these files as processed
            foreach (var fileId in infectedFileIds)
            {
                processedFiles.Add(fileId.ToString());
            }
            SaveProcessedFilesToSession(session, processedFiles);

            // Filter form data to remove infected files
            var filtered = new Dictionary<string, object>();

            foreach (var (key, value) in formData)
            {
                // Check if this field contains file upload data
                var filteredValue = FilterInfectedFilesFromValue(value, infectedFileIds);
                
                // Only include the field if it still has content after filtering
                if (filteredValue != null && !IsEmptyFileList(filteredValue))
                {
                    filtered[key] = filteredValue;
                }
                else if (filteredValue != null)
                {
                    // Field had files but they were all infected, log it
                    logger.LogWarning("Field {FieldKey} contained only infected files, removing from save data", key);
                }
            }

            return filtered;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error filtering infected files for application {ApplicationId}, proceeding with original data", applicationId);
            return formData; // Return original data if filtering fails
        }
    }

    private bool IsMalwareNotification(NotificationDto notification, Guid applicationId)
    {
        if (notification.Type != NotificationType.Warning)
            return false;

        if (notification.Metadata == null || notification.Metadata.Count == 0)
            return false;

        // Check if it's for this application
        if (notification.Metadata.TryGetValue("applicationId", out var appIdObj))
        {
            var appIdStr = appIdObj?.ToString();
            if (Guid.TryParse(appIdStr, out var appId) && appId != applicationId)
                return false;
        }

        // Check for required malware fields
        return notification.Metadata.ContainsKey("fileId") &&
               notification.Metadata.ContainsKey("fileName") &&
               notification.Metadata.ContainsKey("malwareName") &&
               !string.IsNullOrWhiteSpace(notification.Metadata["malwareName"]?.ToString());
    }

    private object? FilterInfectedFilesFromValue(object? value, HashSet<Guid> infectedFileIds)
    {
        if (value == null)
            return null;

        // Handle JSON element (common in serialized data)
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Array)
            {
                try
                {
                    var json = jsonElement.GetRawText();
                    
                    if (string.IsNullOrWhiteSpace(json))
                        return value;

                    // Try to parse as file list first
                    try
                    {
                        var files = JsonSerializer.Deserialize<List<UploadDto>>(json);
                        if (files != null && files.Count > 0)
                        {
                            var cleaned = files.Where(f => !infectedFileIds.Contains(f.Id)).ToList();
                            if (cleaned.Count < files.Count)
                            {
                                logger.LogWarning("Removed {RemovedCount} infected file(s) from field data", 
                                    files.Count - cleaned.Count);
                                return JsonSerializer.Serialize(cleaned);
                            }
                            return value;
                        }
                    }
                    catch { /* Not a file list */ }

                    // If not a file list, might be a collection array - check each item
                    try
                    {
                        var collection = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
                        if (collection != null)
                        {
                            var modified = false;
                            var cleanedCollection = new List<Dictionary<string, object>>();

                            foreach (var item in collection)
                            {
                                var cleanedItem = new Dictionary<string, object>();
                                foreach (var (key, itemValue) in item)
                                {
                                    var cleanedValue = FilterInfectedFilesFromValue(itemValue, infectedFileIds);
                                    if (cleanedValue != null && !IsEmptyFileList(cleanedValue))
                                    {
                                        cleanedItem[key] = cleanedValue;
                                        if (cleanedValue != itemValue)
                                        {
                                            modified = true;
                                        }
                                    }
                                    else if (cleanedValue == null && itemValue == null)
                                    {
                                        cleanedItem[key] = itemValue!;
                                    }
                                    else
                                    {
                                        // Field had files but they were all infected
                                        cleanedItem[key] = "[]"; // Empty array for file fields
                                        modified = true;
                                    }
                                }
                                cleanedCollection.Add(cleanedItem);
                            }

                            if (modified)
                            {
                                logger.LogInformation("Removed infected file(s) from collection data");
                                return JsonSerializer.Serialize(cleanedCollection);
                            }
                        }
                    }
                    catch { /* Not a collection */ }
                }
                catch
                {
                    // Parsing failed, return as-is
                }
            }
            else if (jsonElement.ValueKind == JsonValueKind.String)
            {
                var json = jsonElement.GetString();
                return FilterInfectedFilesFromValue(json, infectedFileIds);
            }
            return value;
        }

        // Handle string (JSON serialized file list or collection)
        if (value is string strValue && !string.IsNullOrWhiteSpace(strValue))
        {
            // Try to parse as file list first
            try
            {
                var files = JsonSerializer.Deserialize<List<UploadDto>>(strValue);
                if (files != null && files.Count > 0)
                {
                    var cleaned = files.Where(f => !infectedFileIds.Contains(f.Id)).ToList();
                    if (cleaned.Count < files.Count)
                    {
                        logger.LogWarning("Removed {RemovedCount} infected file(s) from field data", 
                            files.Count - cleaned.Count);
                        return JsonSerializer.Serialize(cleaned);
                    }
                    return value;
                }
            }
            catch { /* Not a file list */ }

            // Try to parse as collection
            try
            {
                var collection = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(strValue);
                if (collection != null && collection.Count > 0)
                {
                    var modified = false;
                    var cleanedCollection = new List<Dictionary<string, object>>();

                    foreach (var item in collection)
                    {
                        var cleanedItem = new Dictionary<string, object>();
                        foreach (var (key, itemValue) in item)
                        {
                            var cleanedValue = FilterInfectedFilesFromValue(itemValue, infectedFileIds);
                            if (cleanedValue != null && !IsEmptyFileList(cleanedValue))
                            {
                                cleanedItem[key] = cleanedValue;
                                if (cleanedValue != itemValue)
                                {
                                    modified = true;
                                }
                            }
                            else if (cleanedValue == null && itemValue == null)
                            {
                                cleanedItem[key] = itemValue!;
                            }
                            else
                            {
                                // Field had files but they were all infected
                                cleanedItem[key] = "[]"; // Empty array for file fields
                                modified = true;
                            }
                        }
                        cleanedCollection.Add(cleanedItem);
                    }

                    if (modified)
                    {
                        logger.LogInformation("Removed infected file(s) from collection data");
                        return JsonSerializer.Serialize(cleanedCollection);
                    }
                }
            }
            catch { /* Not a collection */ }

            return value;
        }

        // Handle direct list of UploadDto
        if (value is List<UploadDto> uploadList)
        {
            var cleaned = uploadList.Where(f => !infectedFileIds.Contains(f.Id)).ToList();
            if (cleaned.Count < uploadList.Count)
            {
                logger.LogWarning("Removed {RemovedCount} infected file(s) from field data", 
                    uploadList.Count - cleaned.Count);
            }
            return cleaned;
        }

        return value;
    }

    private bool IsEmptyFileList(object? value)
    {
        if (value == null)
            return true;

        if (value is string strValue && string.IsNullOrWhiteSpace(strValue))
            return true;

        if (value is List<UploadDto> list && list.Count == 0)
            return true;

        // Try to parse as JSON array
        try
        {
            var json = value.ToString();
            if (string.IsNullOrWhiteSpace(json) || json == "[]")
                return true;

            var files = JsonSerializer.Deserialize<List<UploadDto>>(json!);
            return files == null || files.Count == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the set of already processed malware file IDs from session
    /// </summary>
    private HashSet<string> GetProcessedFilesFromSession(ISession session)
    {
        var json = session.GetString(ProcessedFilesSessionKey);
        if (string.IsNullOrEmpty(json))
        {
            return new HashSet<string>();
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list != null ? new HashSet<string>(list) : new HashSet<string>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deserializing processed files from session");
            return new HashSet<string>();
        }
    }

    /// <summary>
    /// Saves the set of processed malware file IDs to session
    /// </summary>
    private void SaveProcessedFilesToSession(ISession session, HashSet<string> processedFiles)
    {
        try
        {
            var json = JsonSerializer.Serialize(processedFiles.ToList());
            session.SetString(ProcessedFilesSessionKey, json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving processed files to session");
        }
    }
} 