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
                return RedirectToPage("/RenderForm", new { referenceNumber = ReferenceNumber });
            }

            if (!ApplicationId.HasValue)
            {
                _logger.LogError("ApplicationId not found during submission for reference {ReferenceNumber}", ReferenceNumber);
                return RedirectToPage("/RenderForm", new { referenceNumber = ReferenceNumber });
            }

            try
            {
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
                
                return RedirectToPage("/ApplicationSubmitted", new { referenceNumber = ReferenceNumber });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to submit application {ApplicationId} with reference {ReferenceNumber}", 
                    ApplicationId.Value, ReferenceNumber);
                
                // You might want to show an error message to the user
                // For now, redirect back to preview page
                return RedirectToPage("/ApplicationPreview", new { referenceNumber = ReferenceNumber });
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

        public bool AreAllTasksCompleted()
        {
            if (Template?.TaskGroups == null) return false;

            var allTasks = Template.TaskGroups.SelectMany(g => g.Tasks).ToList();

            foreach (var task in allTasks)
            {
                var taskStatus = GetTaskStatusFromSession(task.TaskId);
                if (taskStatus != Domain.Models.TaskStatus.Completed)
                {
                    return false;
                }
            }

            return allTasks.Any();
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