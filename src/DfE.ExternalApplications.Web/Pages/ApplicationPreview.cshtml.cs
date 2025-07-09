using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Services;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Web.Pages
{
    [ExcludeFromCodeCoverage]
    public class ApplicationPreviewModel : PageModel
    {
        public FormTemplate Template { get; set; }
        [BindProperty(SupportsGet = true, Name = "referenceNumber")] public string ReferenceNumber { get; set; }
        public string TemplateId { get; set; }
        public Guid? ApplicationId { get; set; }
        public Dictionary<string, object> FormData { get; set; } = new();
        public string ApplicationStatus { get; set; } = "InProgress";

        private readonly IFieldRendererService _renderer;
        private readonly IFormTemplateProvider _templateProvider;
        private readonly IApplicationResponseService _applicationResponseService;
        private readonly IApplicationsClient _applicationsClient;
        private readonly ILogger<ApplicationPreviewModel> _logger;

        public ApplicationPreviewModel(
            IFieldRendererService renderer,
            IFormTemplateProvider templateProvider,
            IApplicationResponseService applicationResponseService,
            IApplicationsClient applicationsClient,
            ILogger<ApplicationPreviewModel> logger)
        {
            _renderer = renderer;
            _templateProvider = templateProvider;
            _applicationResponseService = applicationResponseService;
            _applicationsClient = applicationsClient;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            TemplateId = HttpContext.Session.GetString("TemplateId") ?? string.Empty;
            await EnsureApplicationIdAsync();
            await LoadTemplateAsync();
            LoadFormDataFromSession();
            LoadApplicationStatus();

            // If application is not in progress, still allow viewing but with restrictions
            if (!IsApplicationEditable())
            {
                // Allow viewing but change links will be hidden
                return Page();
            }

            // Check if all tasks are completed before allowing access (for InProgress applications)
            if (!AreAllTasksCompleted())
            {
                return RedirectToPage("/RenderForm", new { referenceNumber = ReferenceNumber });
            }

            return Page();
        }

        public async Task<IActionResult> OnPostSubmitAsync()
        {
            TemplateId = HttpContext.Session.GetString("TemplateId") ?? string.Empty;
            await EnsureApplicationIdAsync();
            await LoadTemplateAsync();
            LoadFormDataFromSession();

            // Check if all tasks are completed before allowing submission
            if (!AreAllTasksCompleted())
            {
                _logger.LogWarning("Cannot submit application {ReferenceNumber} - not all tasks completed", ReferenceNumber);
                ModelState.AddModelError("", "All sections must be completed before you can submit your application.");
                return Page();
            }

            if (!ApplicationId.HasValue)
            {
                _logger.LogError("ApplicationId not found during submission for reference {ReferenceNumber}", ReferenceNumber);
                ModelState.AddModelError("", "Application not found. Please try again.");
                return Page();
            }

            try
            {
                _logger.LogInformation("Attempting to submit application {ApplicationId} with reference {ReferenceNumber}", 
                    ApplicationId.Value, ReferenceNumber);

                // Submit the application via API
                var submittedApplication = await _applicationsClient.SubmitApplicationAsync(ApplicationId.Value);
                
                // Update session with new application status
                if (submittedApplication != null)
                {
                    var statusKey = $"ApplicationStatus_{ApplicationId.Value}";
                    HttpContext.Session.SetString(statusKey, submittedApplication.Status?.ToString() ?? "Submitted");
                    _logger.LogInformation("Successfully submitted application {ApplicationId} with reference {ReferenceNumber}", 
                        ApplicationId.Value, ReferenceNumber);
                }
                else
                {
                    _logger.LogWarning("Submit API returned null for application {ApplicationId}", ApplicationId.Value);
                }
                
                return RedirectToPage("/ApplicationSubmitted", new { referenceNumber = ReferenceNumber });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to submit application {ApplicationId} with reference {ReferenceNumber}", 
                    ApplicationId.Value, ReferenceNumber);
                
                ModelState.AddModelError("", $"An error occurred while submitting your application: {ex.Message}. Please try again.");
                return Page();
            }

        }

        private void LoadFormDataFromSession()
        {
            FormData = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
        }

        private void LoadApplicationStatus()
        {
            if (ApplicationId.HasValue)
            {
                var statusKey = $"ApplicationStatus_{ApplicationId.Value}";
                ApplicationStatus = HttpContext.Session.GetString(statusKey) ?? "InProgress";
            }
            else
            {
                ApplicationStatus = "InProgress";
            }
        }

        public bool IsApplicationEditable()
        {
            return ApplicationStatus.Equals("InProgress", StringComparison.OrdinalIgnoreCase);
        }

        private async Task LoadTemplateAsync()
        {
            Template = await _templateProvider.GetTemplateAsync(TemplateId);
        }

        public string GetFieldValue(string fieldId)
        {
            if (FormData.TryGetValue(fieldId, out var value))
            {
                if (value == null)
                {
                    return string.Empty;
                }

                // If it's already a string, return it
                if (value is string stringValue)
                {
                    return stringValue;
                }

                // If it's an object (like from autocomplete), serialize it to JSON
                try
                {
                    return JsonSerializer.Serialize(value);
                }
                catch
                {
                    return value.ToString() ?? string.Empty;
                }
            }
            return string.Empty;
        }

        public string GetFormattedFieldValue(string fieldId)
        {
            var fieldValue = GetFieldValue(fieldId);
            
            if (string.IsNullOrEmpty(fieldValue))
            {
                return string.Empty;
            }

            // Try to format as autocomplete data if it looks like JSON
            if (fieldValue.StartsWith("{") || fieldValue.StartsWith("["))
            {
                return FormatAutocompleteValue(fieldValue);
            }

            return fieldValue;
        }

        private string FormatAutocompleteValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            try
            {
                using (var doc = JsonDocument.Parse(value))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var displayValues = new List<string>();
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            displayValues.Add(FormatSingleAutocompleteValue(element));
                        }
                        return string.Join(", ", displayValues);
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        return FormatSingleAutocompleteValue(doc.RootElement);
                    }
                }
            }
            catch
            {
                // If not JSON, return as is
            }

            return value;
        }

        private string FormatSingleAutocompleteValue(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                string name = "";
                string ukprn = "";

                if (element.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String)
                {
                    name = nameProperty.GetString() ?? "";
                }

                if (element.TryGetProperty("ukprn", out var ukprnProperty))
                {
                    if (ukprnProperty.ValueKind == JsonValueKind.String)
                    {
                        ukprn = ukprnProperty.GetString() ?? "";
                    }
                    else if (ukprnProperty.ValueKind == JsonValueKind.Number)
                    {
                        ukprn = ukprnProperty.GetInt64().ToString();
                    }
                }

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(ukprn))
                {
                    return $"{name} (UKPRN: {ukprn})";
                }
                else if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? "";
            }

            return element.ToString();
        }

        public bool HasFieldValue(string fieldId)
        {
            var value = GetFieldValue(fieldId);
            return !string.IsNullOrWhiteSpace(value);
        }

        public Domain.Models.TaskStatus GetTaskStatusFromSession(string taskId)
        {
            return CalculateTaskStatus(taskId);
        }

        /// <summary>
        /// Calculate task status based on actual form data completion
        /// </summary>
        private Domain.Models.TaskStatus CalculateTaskStatus(string taskId)
        {
            // If application is submitted, all tasks are completed
            if (ApplicationStatus.Equals("Submitted", StringComparison.OrdinalIgnoreCase))
            {
                return Domain.Models.TaskStatus.Completed;
            }
            
            // First check if task is explicitly marked as completed
            if (ApplicationId.HasValue)
            {
                var sessionKey = $"TaskStatus_{ApplicationId.Value}_{taskId}";
                var statusString = HttpContext.Session.GetString(sessionKey);
                
                if (!string.IsNullOrEmpty(statusString) && 
                    Enum.TryParse<Domain.Models.TaskStatus>(statusString, out var explicitStatus) &&
                    explicitStatus == Domain.Models.TaskStatus.Completed)
                {
                    return Domain.Models.TaskStatus.Completed;
                }
            }
            
            // Get all form data from FormData dictionary 
            var formData = FormData ?? new Dictionary<string, object>();
            
            // Find the task in the template
            var task = Template?.TaskGroups?
                .SelectMany(g => g.Tasks)
                .FirstOrDefault(t => t.TaskId == taskId);
                
            if (task == null)
            {
                return Domain.Models.TaskStatus.NotStarted;
            }
            
            // Check if any fields in this task have been completed
            var taskFieldIds = task.Pages
                .SelectMany(p => p.Fields)
                .Select(f => f.FieldId)
                .ToList();
                
            var hasAnyFieldCompleted = taskFieldIds.Any(fieldId => 
                formData.ContainsKey(fieldId) && 
                !string.IsNullOrWhiteSpace(formData[fieldId]?.ToString()));
            
            if (hasAnyFieldCompleted)
            {
                return Domain.Models.TaskStatus.InProgress;
            }
            
            return Domain.Models.TaskStatus.NotStarted;
        }

        public bool AreAllTasksCompleted()
        {
            if (Template?.TaskGroups == null) 
            {
                _logger.LogWarning("AreAllTasksCompleted: Template or TaskGroups is null");
                return false;
            }

            var allTasks = Template.TaskGroups.SelectMany(g => g.Tasks).ToList();
            _logger.LogDebug("AreAllTasksCompleted: Found {TaskCount} total tasks", allTasks.Count);

            foreach (var task in allTasks)
            {
                var taskStatus = GetTaskStatusFromSession(task.TaskId);
                _logger.LogDebug("Task {TaskId} status: {Status}", task.TaskId, taskStatus);
                
                if (taskStatus != Domain.Models.TaskStatus.Completed)
                {
                    _logger.LogWarning("AreAllTasksCompleted: Task {TaskId} is not completed (status: {Status})", task.TaskId, taskStatus);
                    return false;
                }
            }

            var result = allTasks.Any();
            _logger.LogInformation("AreAllTasksCompleted: {Result} (checked {TaskCount} tasks)", result, allTasks.Count);
            return result;
        }

        private async Task EnsureApplicationIdAsync()
        {
            // First try to get ApplicationId from session (for newly created applications)
            var applicationIdString = HttpContext.Session.GetString("ApplicationId");
            var sessionReference = HttpContext.Session.GetString("ApplicationReference");

            if (!string.IsNullOrEmpty(applicationIdString) &&
                !string.IsNullOrEmpty(sessionReference) &&
                sessionReference == ReferenceNumber)
            {
                if (Guid.TryParse(applicationIdString, out var sessionAppId))
                {
                    ApplicationId = sessionAppId;
                    return;
                }
            }

            // If not in session or different reference, try to get from API
            try
            {
                var application = await _applicationsClient.GetApplicationByReferenceAsync(ReferenceNumber);

                if (application != null)
                {
                    ApplicationId = application.ApplicationId;
                    // Store in session for future use
                    HttpContext.Session.SetString("ApplicationId", application.ApplicationId.ToString());
                    HttpContext.Session.SetString("ApplicationReference", application.ApplicationReference);
                    
                    // Store application status in session
                    if (application.Status != null)
                    {
                        var statusKey = $"ApplicationStatus_{application.ApplicationId}";
                        HttpContext.Session.SetString(statusKey, application.Status.ToString());
                    }

                    // Load existing response data into session for existing applications
                    await LoadResponseDataIntoSessionAsync(application);
                }
                else
                {
                    _logger.LogWarning("Could not find application with reference {ReferenceNumber}", ReferenceNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve application information for reference {ReferenceNumber}", ReferenceNumber);
            }
        }

        private async Task LoadResponseDataIntoSessionAsync(ApplicationDto application)
        {
            if (application.LatestResponse?.ResponseBody == null)
            {
                _logger.LogInformation("No existing response data found for application {ApplicationReference}", application.ApplicationReference);
                return;
            }

            try
            {
                _logger.LogInformation("Loading response data for application {ApplicationReference}. Response body: {ResponseBody}", 
                    application.ApplicationReference, application.LatestResponse.ResponseBody);

                string responseJson;
                
                try
                {
                    var decodedBytes = Convert.FromBase64String(application.LatestResponse.ResponseBody);
                    responseJson = System.Text.Encoding.UTF8.GetString(decodedBytes);
                    _logger.LogInformation("Successfully decoded Base64 response for application {ApplicationReference}", application.ApplicationReference);
                }
                catch (FormatException)
                {
                    // Not Base64, assume it's direct JSON (backward compatibility)
                    responseJson = application.LatestResponse.ResponseBody;
                    _logger.LogInformation("Using direct JSON response for application {ApplicationReference} (backward compatibility)", application.ApplicationReference);
                }

                // Parse the response body JSON
                var responseData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);

                if (responseData == null)
                {
                    _logger.LogWarning("Failed to parse response JSON for application {ApplicationReference}", application.ApplicationReference);
                    return;
                }

                var formDataDict = new Dictionary<string, object>();

                foreach (var kvp in responseData)
                {
                    try
                    {
                        // Handle both simple and complex field structures
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
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process field {FieldName} for application {ApplicationReference}", 
                            kvp.Key, application.ApplicationReference);
                    }
                }

                // Store in session using the same key structure as form submission
                _applicationResponseService.StoreFormDataInSession(formDataDict, HttpContext.Session);
                _applicationResponseService.SetCurrentAccumulatedApplicationId(application.ApplicationId, HttpContext.Session);

                _logger.LogInformation("Successfully loaded {FieldCount} fields from API into session for application {ApplicationReference}", 
                    formDataDict.Count, application.ApplicationReference);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load response data for application {ApplicationReference}: {ErrorMessage}", 
                    application.ApplicationReference, ex.Message);
            }
        }

        private object GetJsonElementValue(JsonElement element)
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