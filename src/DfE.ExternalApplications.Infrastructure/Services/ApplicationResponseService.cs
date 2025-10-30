using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using DfE.ExternalApplications.Application.Interfaces;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Infrastructure.Services;

public class ApplicationResponseService(
    IApplicationsClient applicationsClient,
    ILogger<ApplicationResponseService> logger)
    : IApplicationResponseService
{
    private const string SessionKeyFormData = "AccumulatedFormData";

    public async Task SaveApplicationResponseAsync(Guid applicationId, Dictionary<string, object> formData, ISession session, CancellationToken cancellationToken = default)
    {
        try
        {
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

} 