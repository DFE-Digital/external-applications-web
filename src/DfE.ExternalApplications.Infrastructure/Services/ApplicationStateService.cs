using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Infrastructure.Services
{
    /// <summary>
    /// Implementation of application state service for managing application state, status, and session data
    /// </summary>
    public class ApplicationStateService(
        IApplicationsClient applicationsClient,
        IApplicationResponseService applicationResponseService,
        ILogger<ApplicationStateService> logger)
        : IApplicationStateService
    {
        public async Task<(Guid? ApplicationId, ApplicationDto? Application)> EnsureApplicationIdAsync(string referenceNumber, ISession session)
        {
            // First check if we have template schema stored in session for this reference
            var templateSchemaKey = $"TemplateSchema_{referenceNumber}";
            var templateVersionIdKey = $"TemplateVersionId_{referenceNumber}";
            var templateVersionNoKey = $"TemplateVersionNo_{referenceNumber}";
            var storedTemplateSchema = session.GetString(templateSchemaKey);
            var storedTemplateVersionId = session.GetString(templateVersionIdKey);
            var storedTemplateId = session.GetString("TemplateId");
            var storedTemplateVersionNo = session.GetString(templateVersionNoKey);

            // Check if we have basic application data in session
            var applicationIdString = session.GetString("ApplicationId");
            var sessionReference = session.GetString("ApplicationReference");

            if (!string.IsNullOrEmpty(applicationIdString) &&
                !string.IsNullOrEmpty(sessionReference) &&
                sessionReference == referenceNumber)
            {
                if (Guid.TryParse(applicationIdString, out var sessionAppId))
                {
                    ApplicationDto? currentApplication = null;
                    
                    // If we have template schema in session, create a minimal ApplicationDto
                    if (!string.IsNullOrEmpty(storedTemplateSchema) && !string.IsNullOrEmpty(storedTemplateVersionId))
                    {
                        currentApplication = new ApplicationDto
                        {
                            ApplicationId = sessionAppId,
                            ApplicationReference = sessionReference,
                            TemplateVersionId = Guid.Parse(storedTemplateVersionId),
                            TemplateSchema = new TemplateSchemaDto
                            {
                                JsonSchema = storedTemplateSchema,
                                TemplateVersionId = new Guid(storedTemplateVersionId),
                                TemplateId = new Guid(storedTemplateId),
                                VersionNumber = storedTemplateVersionNo ?? String.Empty
                            }
                        };

                        logger.LogDebug("Using cached template schema for application {ApplicationId} with template version {TemplateVersionId}", 
                            sessionAppId, storedTemplateVersionId);

                        return (sessionAppId, currentApplication);
                    }

                }
            }

            // If not in session or incomplete data, fetch from API
            try
            {
                var application = await applicationsClient.GetApplicationByReferenceAsync(referenceNumber);

                if (application != null)
                {
                    // Store application data in session for future use
                    session.SetString("ApplicationId", application.ApplicationId.ToString());
                    session.SetString("ApplicationReference", application.ApplicationReference);

                    // Store template schema in session for future use
                    if (application.TemplateSchema?.JsonSchema != null)
                    {
                        session.SetString(templateSchemaKey, application.TemplateSchema.JsonSchema);
                        session.SetString(templateVersionIdKey, application.TemplateVersionId.ToString());
                        session.SetString(templateVersionNoKey, application.TemplateSchema.VersionNumber);

                        logger.LogDebug("Cached template schema for reference {ReferenceNumber} with template version {TemplateVersionId}", 
                            referenceNumber, application.TemplateVersionId);
                    }

                    // Store application status in session
                    if (application.Status != null)
                    {
                        var statusKey = $"ApplicationStatus_{application.ApplicationId}";
                        session.SetString(statusKey, application.Status.ToString());
                    }

                    // Store application lead applicant in session
                    var leadApplicantNameKey = $"ApplicationLeadApplicantName_{application.ApplicationId}";
                    var leadApplicantEmailKey = $"ApplicationLeadApplicantEmail_{application.ApplicationId}";
                    var leadApplicantUserIdKey = $"ApplicationLeadApplicantUserId_{application.ApplicationId}";
                    var applicationFormVersionKey = $"ApplicationFormVersion_{application.ApplicationId}";

                    session.SetString(leadApplicantNameKey, application.CreatedBy!.Name);
                    session.SetString(leadApplicantEmailKey, application.CreatedBy.Email);
                    session.SetString(leadApplicantUserIdKey, application.CreatedBy.UserId.ToString());
                    session.SetString(applicationFormVersionKey, string.IsNullOrEmpty(application.TemplateSchema?.VersionNumber) ? "N/A" : application.TemplateSchema?.VersionNumber!);

                    // Load existing response data into session for existing applications
                    await LoadResponseDataIntoSessionAsync(application, session);
                    
                    logger.LogDebug("Loaded application {ApplicationId} from API with template version {TemplateVersionId}", 
                        application.ApplicationId, application.TemplateVersionId);
                    
                    return (application.ApplicationId, application);
                }
                else
                {
                    logger.LogWarning("Could not find application with reference {ReferenceNumber}", referenceNumber);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve application information from API for reference {ReferenceNumber}", referenceNumber);
            }

            logger.LogWarning("Could not determine ApplicationId for reference {ReferenceNumber}", referenceNumber);
            return (null, null);
        }

        public async Task LoadResponseDataIntoSessionAsync(ApplicationDto application, ISession session)
        {
            if (application.LatestResponse?.ResponseBody == null)
            {
                logger.LogInformation("No existing response data found for application {ApplicationReference}", application.ApplicationReference);
                return;
            }

            try
            {
                logger.LogInformation("Loading response data for application {ApplicationReference}.",
                    application.ApplicationReference);

                var responseJson = application.LatestResponse.ResponseBody;

                // Parse the response body JSON
                var responseData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);

                if (responseData == null)
                {
                    logger.LogWarning("Failed to parse response JSON for application {ApplicationReference}", application.ApplicationReference);
                    return;
                }

                var formDataDict = new Dictionary<string, object>();

                foreach (var kvp in responseData)
                {
                    try
                    {
                        // Check if this is a task status field
                        if (kvp.Key.StartsWith("TaskStatus_"))
                        {
                            // Extract task status and restore to session
                            var taskId = kvp.Key.Substring("TaskStatus_".Length);
                            string statusValue = string.Empty;
                            
                            if (kvp.Value.ValueKind == JsonValueKind.Object && kvp.Value.TryGetProperty("value", out var statusElement))
                            {
                                statusValue = statusElement.GetString() ?? string.Empty;
                            }
                            else
                            {
                                statusValue = kvp.Value.GetString() ?? string.Empty;
                            }
                            
                            if (!string.IsNullOrEmpty(statusValue))
                            {
                                applicationResponseService.SaveTaskStatusToSession(application.ApplicationId, taskId, statusValue, session);
                                logger.LogDebug("Restored task status: {TaskId} = {Status}", taskId, statusValue);
                            }
                        }
                        else
                        {
                            // Handle form field data
                            if (kvp.Value.ValueKind == JsonValueKind.Object && kvp.Value.TryGetProperty("value", out var valueElement))
                            {
                                // Complex structure: {"field1": {"value": "actual_value", "completed": true}}
                                formDataDict[kvp.Key] = GetJsonElementValue(valueElement);
                            }
                            else
                            {
                                // Simple structure: {"field1": "actual_value"}
                                formDataDict[kvp.Key] = GetJsonElementValue(kvp.Value);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to process field {FieldName} for application {ApplicationReference}",
                            kvp.Key, application.ApplicationReference);
                    }
                }

                // Store in session using the same key structure as form submission
                applicationResponseService.StoreFormDataInSession(formDataDict, session);
                applicationResponseService.SetCurrentAccumulatedApplicationId(application.ApplicationId, session);

                logger.LogInformation("Successfully loaded {FieldCount} fields from API into session for application {ApplicationReference}",
                    formDataDict.Count, application.ApplicationReference);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load response data for application {ApplicationReference}: {ErrorMessage}",
                    application.ApplicationReference, ex.Message);
            }
        }

        public string GetApplicationStatus(Guid? applicationId, ISession session)
        {
            if (applicationId.HasValue)
            {
                var statusKey = $"ApplicationStatus_{applicationId.Value}";
                return session.GetString(statusKey) ?? "InProgress";
            }
            return "InProgress";
        }

        public bool IsApplicationEditable(string applicationStatus)
        {
            return applicationStatus.Equals("InProgress", StringComparison.OrdinalIgnoreCase);
        }

        public Domain.Models.TaskStatus CalculateTaskStatus(string taskId, FormTemplate template, Dictionary<string, object> formData, Guid? applicationId, ISession session, string applicationStatus)
        {
            // If application is submitted, all tasks are completed
            if (applicationStatus.Equals("Submitted", StringComparison.OrdinalIgnoreCase))
            {
                return Domain.Models.TaskStatus.Completed;
            }
            
            // First check if task is explicitly marked as completed
            if (applicationId.HasValue)
            {
                var sessionKey = $"TaskStatus_{applicationId.Value}_{taskId}";
                var statusString = session.GetString(sessionKey);
                
                if (!string.IsNullOrEmpty(statusString) && 
                    Enum.TryParse<Domain.Models.TaskStatus>(statusString, out var explicitStatus) &&
                    explicitStatus == Domain.Models.TaskStatus.Completed)
                {
                    return Domain.Models.TaskStatus.Completed;
                }
            }
            
            // Find the task in the template
            var task = template?.TaskGroups?
                .SelectMany(g => g.Tasks)
                .FirstOrDefault(t => t.TaskId == taskId);
                
            if (task == null)
            {
                return Domain.Models.TaskStatus.NotStarted;
            }
            
            // Check if any fields in this task have been completed
            var taskFieldIds = new List<string>();
            
            // For regular tasks, get field IDs from pages
            if (task.Pages != null)
            {
                taskFieldIds.AddRange(task.Pages
                    .SelectMany(p => p.Fields)
                    .Select(f => f.FieldId));
            }
            
            // For multi-collection flow tasks, also check collection field IDs
            if (task.Summary?.Mode?.Equals("multiCollectionFlow", StringComparison.OrdinalIgnoreCase) == true &&
                task.Summary.Flows != null)
            {
                taskFieldIds.AddRange(task.Summary.Flows.Select(f => f.FieldId));
            }
                
            var hasAnyFieldCompleted = taskFieldIds.Any(fieldId => 
                formData.ContainsKey(fieldId) && 
                !string.IsNullOrWhiteSpace(formData[fieldId]?.ToString()));
            
            if (hasAnyFieldCompleted)
            {
                return Domain.Models.TaskStatus.InProgress;
            }
            
            return Domain.Models.TaskStatus.NotStarted;
        }

        public async Task SaveTaskStatusAsync(Guid applicationId, string taskId, Domain.Models.TaskStatus status, ISession session)
        {
            // Save task status to session
            applicationResponseService.SaveTaskStatusToSession(applicationId, taskId, status.ToString(), session);
            
            // Save all accumulated data (including task status) to API
            var formData = new Dictionary<string, object>(); // Empty form data since we're just updating task status
            await applicationResponseService.SaveApplicationResponseAsync(applicationId, formData, session);
        }

        public bool AreAllTasksCompleted(FormTemplate template, Dictionary<string, object> formData, Guid? applicationId, ISession session, string applicationStatus)
        {
            if (template?.TaskGroups == null)
            {
                return false;
            }

            var allTasks = template.TaskGroups.SelectMany(g => g.Tasks).ToList();
            
            return allTasks.All(task => 
                CalculateTaskStatus(task.TaskId, template, formData, applicationId, session, applicationStatus) == Domain.Models.TaskStatus.Completed);
        }

        public object GetJsonElementValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    return element.GetDecimal();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null;
                default:
                    return element.ToString();
            }
        }
    }
} 