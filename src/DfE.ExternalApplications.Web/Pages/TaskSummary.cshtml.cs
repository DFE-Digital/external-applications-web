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
    public class TaskSummaryModel : PageModel
    {
        public FormTemplate Template { get; set; }
        [BindProperty(SupportsGet = true, Name = "referenceNumber")] public string ReferenceNumber { get; set; }
        [BindProperty(SupportsGet = true, Name = "taskId")] public string TaskId { get; set; }
        [BindProperty] public bool IsTaskCompleted { get; set; }
        public string TemplateId { get; set; }
        public Guid? ApplicationId { get; set; }
        public string ApplicationStatus { get; set; } = "InProgress";

        public TaskGroup CurrentGroup { get; set; }
        public Domain.Models.Task CurrentTask { get; set; }
        public Dictionary<string, object> FormData { get; set; } = new();

        private readonly IFieldRendererService _renderer;
        private readonly IFormTemplateProvider _templateProvider;
        private readonly IApplicationResponseService _applicationResponseService;
        private readonly IApplicationsClient _applicationsClient;
        private readonly ILogger<TaskSummaryModel> _logger;

        public TaskSummaryModel(
            IFieldRendererService renderer,
            IFormTemplateProvider templateProvider,
            IApplicationResponseService applicationResponseService,
            IApplicationsClient applicationsClient,
            ILogger<TaskSummaryModel> logger)
        {
            _renderer = renderer;
            _templateProvider = templateProvider;
            _applicationResponseService = applicationResponseService;
            _applicationsClient = applicationsClient;
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            TemplateId = HttpContext.Session.GetString("TemplateId") ?? string.Empty;
            await EnsureApplicationIdAsync();
            await LoadTemplateAsync();
            InitializeCurrentTask(TaskId);
            LoadFormDataFromSession();
            LoadApplicationStatus();
            
            // Check if task is already marked as completed from session
            IsTaskCompleted = GetTaskStatusFromSession(TaskId) == Domain.Models.TaskStatus.Completed;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            TemplateId = HttpContext.Session.GetString("TemplateId") ?? string.Empty;
            await EnsureApplicationIdAsync();
            await LoadTemplateAsync();
            InitializeCurrentTask(TaskId);
            LoadApplicationStatus();

            // Prevent editing if application is not editable
            if (!IsApplicationEditable())
            {
                return RedirectToPage("/ApplicationPreview", new { referenceNumber = ReferenceNumber });
            }

            // Update task status based on checkbox
            var newStatus = IsTaskCompleted ? Domain.Models.TaskStatus.Completed : Domain.Models.TaskStatus.InProgress;
            
            // Update the task status in the template (this will need to be persisted)
            CurrentTask.TaskStatusString = newStatus.ToString();
            
            // Save the task status change
            if (ApplicationId.HasValue)
            {
                try
                {
                    // Save task status to the API (we'll need to implement this)
                    await SaveTaskStatusAsync(ApplicationId.Value, TaskId, newStatus);
                    _logger.LogInformation("Successfully updated task status for Application {ApplicationId}, Task {TaskId} to {Status}",
                        ApplicationId.Value, TaskId, newStatus);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save task status for Application {ApplicationId}, Task {TaskId}",
                        ApplicationId.Value, TaskId);
                }
            }

            return RedirectToPage("/RenderForm", new { referenceNumber = ReferenceNumber });
        }

        private void LoadFormDataFromSession()
        {
            FormData = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
        }

        private async Task LoadTemplateAsync()
        {
            Template = await _templateProvider.GetTemplateAsync(TemplateId);
        }

        private void InitializeCurrentTask(string taskId)
        {
            var allTasks = Template.TaskGroups
                .SelectMany(g => g.Tasks.Select(t => new { Group = g, Task = t }))
                .ToList();

            var taskPair = allTasks.FirstOrDefault(x => x.Task.TaskId == taskId);
            
            if (taskPair == null)
            {
                throw new InvalidOperationException($"Task with ID '{taskId}' not found.");
            }

            CurrentGroup = taskPair.Group;
            CurrentTask = taskPair.Task;
        }

        public string GetFieldValue(string fieldId)
        {
            if (FormData.TryGetValue(fieldId, out var value))
            {
                return value?.ToString() ?? string.Empty;
            }
            return string.Empty;
        }

        public bool HasFieldValue(string fieldId)
        {
            var value = GetFieldValue(fieldId);
            return !string.IsNullOrWhiteSpace(value);
        }

        public Domain.Models.TaskStatus GetTaskStatusFromSession(string taskId)
        {
            if (ApplicationId.HasValue)
            {
                var sessionKey = $"TaskStatus_{ApplicationId.Value}_{taskId}";
                var statusString = HttpContext.Session.GetString(sessionKey);
                
                if (!string.IsNullOrEmpty(statusString) && 
                    Enum.TryParse<Domain.Models.TaskStatus>(statusString, out var status))
                {
                    return status;
                }
            }
            
            return Domain.Models.TaskStatus.NotStarted;
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

        private async Task SaveTaskStatusAsync(Guid applicationId, string taskId, Domain.Models.TaskStatus status)
        {
            // Save task status to session
            _applicationResponseService.SaveTaskStatusToSession(applicationId, taskId, status.ToString(), HttpContext.Session);
            
            // Save all accumulated data (including task status) to API
            var formData = new Dictionary<string, object>(); // Empty form data since we're just updating task status
            await _applicationResponseService.SaveApplicationResponseAsync(applicationId, formData, HttpContext.Session);
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
                // Parse the response body JSON
                var responseData = JsonSerializer.Deserialize<Dictionary<string, object>>(application.LatestResponse.ResponseBody);

                if (responseData != null && responseData.Any())
                {
                    // Extract field values from the response structure
                    var formData = new Dictionary<string, object>();

                    foreach (var kvp in responseData)
                    {
                        // Check if this is a task status field
                        if (kvp.Key.StartsWith("TaskStatus_"))
                        {
                            // Extract task ID from the key (format: TaskStatus_{TaskId})
                            var taskId = kvp.Key.Substring("TaskStatus_".Length);
                            
                            if (kvp.Value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
                            {
                                if (jsonElement.TryGetProperty("value", out var valueElement))
                                {
                                    var statusValue = valueElement.GetString();
                                    if (!string.IsNullOrEmpty(statusValue) && ApplicationId.HasValue)
                                    {
                                        // Save task completion status back to session
                                        _applicationResponseService.SaveTaskStatusToSession(ApplicationId.Value, taskId, statusValue, HttpContext.Session);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Regular field data
                            if (kvp.Value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
                            {
                                if (jsonElement.TryGetProperty("value", out var valueElement))
                                {
                                    var value = valueElement.ValueKind switch
                                    {
                                        JsonValueKind.String => valueElement.GetString() ?? string.Empty,
                                        JsonValueKind.Number => valueElement.GetDecimal().ToString(),
                                        JsonValueKind.True => "true",
                                        JsonValueKind.False => "false",
                                        JsonValueKind.Null => string.Empty,
                                        _ => valueElement.ToString()
                                    };
                                    formData[kvp.Key] = value;
                                }
                            }
                        }
                    }

                    // Clear existing session data and load the API data
                    _applicationResponseService.ClearAccumulatedFormData(HttpContext.Session);
                    _applicationResponseService.AccumulateFormData(formData, HttpContext.Session);

                    _logger.LogInformation("Loaded {Count} field responses from API into session for application {ApplicationReference}",
                        formData.Count, application.ApplicationReference);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse response body for application {ApplicationReference}", application.ApplicationReference);
            }
        }
    }
} 