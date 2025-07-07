using DfE.CoreLibs.Contracts.ExternalApplications.Models.Request;
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
            
            logger.LogInformation("Successfully saved application response for {ApplicationId}", applicationId);
        }
        catch (GovUK.Dfe.ExternalApplications.Api.Client.Contracts.ExternalApplicationsException ex) when (ex.Message.Contains("The HTTP status code of the response was not expected (200)"))
        {
            logger.LogInformation("Application response saved successfully for {ApplicationId} (API returned 200 instead of expected 201)", applicationId);
            logger.LogDebug("API Response: {ApiResponse}", ex.Response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save application response for application {ApplicationId}", applicationId);
            throw;
        }
    }

    public void AccumulateFormData(Dictionary<string, object> newData, ISession session)
    {
        var existingData = GetAccumulatedFormData(session);
        
        // Merge new data into existing data
        foreach (var kvp in newData)
        {
            existingData[kvp.Key] = kvp.Value;
        }
        
        // Save back to session
        var jsonString = JsonSerializer.Serialize(existingData);
        session.SetString(SessionKeyFormData, jsonString);
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
            return jsonElement.ValueKind switch
            {
                JsonValueKind.String => jsonElement.GetString() ?? string.Empty,
                JsonValueKind.Array => jsonElement.GetArrayLength() == 1 
                    ? jsonElement[0].GetString() ?? string.Empty 
                    : jsonElement.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray(),
                JsonValueKind.Number => jsonElement.GetDecimal().ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => value.ToString() ?? string.Empty
            };
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