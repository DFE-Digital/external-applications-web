using System.Text.Json;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Request;
using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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
            
            var responseJson = TransformToResponseJson(allFormData, Enumerable.Empty<Field>());
            
            var request = new AddApplicationResponseRequest(responseJson);
            await applicationsClient.AddApplicationResponseAsync(applicationId, request, cancellationToken);
            
            logger.LogInformation("Successfully saved application response for Application ID: {ApplicationId}", applicationId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save application response for Application ID: {ApplicationId}", applicationId);
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
            return JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString) 
                   ?? new Dictionary<string, object>();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize accumulated form data from session. Starting fresh.");
            return new Dictionary<string, object>();
        }
    }

    public string TransformToResponseJson(Dictionary<string, object> formData, IEnumerable<Field> pageFields)
    {
        var responseData = new Dictionary<string, object>();

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

        return JsonSerializer.Serialize(responseData, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }
} 