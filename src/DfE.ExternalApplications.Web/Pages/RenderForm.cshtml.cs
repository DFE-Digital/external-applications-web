using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Services;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Text.Json;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Web.Pages
{
    [ExcludeFromCodeCoverage]
    public class RenderFormModel : PageModel
    {
        public FormTemplate Template { get; set; }
        [BindProperty(SupportsGet = true, Name = "referenceNumber")] public string ReferenceNumber { get; set; }
        [BindProperty] public Dictionary<string, object> Data { get; set; } = new();
        public string TemplateId { get; set; }
        public Guid? ApplicationId { get; set; }
        [BindProperty] public string CurrentPageId { get; set; }
        public string ApplicationStatus { get; set; } = "InProgress";

        public TaskGroup CurrentGroup { get; set; }
        public Domain.Models.Task CurrentTask { get; set; }
        public Domain.Models.Page CurrentPage { get; set; }

        private readonly IFieldRendererService _renderer;
        private readonly IFormTemplateProvider _templateProvider;
        private readonly IApplicationResponseService _applicationResponseService;
        private readonly IApplicationsClient _applicationsClient;
        private readonly IAutocompleteService _autocompleteService;
        private readonly ILogger<RenderFormModel> _logger;

        public RenderFormModel(
            IFieldRendererService renderer, 
            IFormTemplateProvider templateProvider,
            IApplicationResponseService applicationResponseService,
            IApplicationsClient applicationsClient,
            IAutocompleteService autocompleteService,
            ILogger<RenderFormModel> logger)
        {
            _renderer = renderer;
            _templateProvider = templateProvider;
            _applicationResponseService = applicationResponseService;
            _applicationsClient = applicationsClient;
            _autocompleteService = autocompleteService;
            _logger = logger;
        }

        public async Task OnGetAsync(string pageId)
        {
            TemplateId = HttpContext.Session.GetString("TemplateId") ?? string.Empty;
            await EnsureApplicationIdAsync();
            CurrentPageId = pageId;
            await LoadTemplateAsync();
            LoadApplicationStatus();
            
            // If application is not editable and trying to access a specific page, redirect to preview
            if (!IsApplicationEditable() && !string.IsNullOrEmpty(pageId))
            {
                Response.Redirect($"~/render-form/{ReferenceNumber}/preview");
                return;
            }
            
            if (!string.IsNullOrEmpty(pageId))
            {
                InitializeCurrentPage(CurrentPageId);
            }
            
            // Check if we need to clear session data for a new application
            CheckAndClearSessionForNewApplication();
            
            // Load accumulated form data from session to pre-populate fields
            LoadAccumulatedDataFromSession();
        }

        public async Task<IActionResult> OnPostPageAsync()
        {
            TemplateId = HttpContext.Session.GetString("TemplateId") ?? string.Empty;
            await EnsureApplicationIdAsync();
            await LoadTemplateAsync();
            LoadApplicationStatus();
            
            // Prevent editing if application is not editable
            if (!IsApplicationEditable())
            {
                return RedirectToPage("/ApplicationPreview", new { referenceNumber = ReferenceNumber });
            }
            
            InitializeCurrentPage(CurrentPageId);

            foreach (var key in Request.Form.Keys)
            {
                var match = Regex.Match(key, @"^Data\[(.+?)\]$", RegexOptions.None, TimeSpan.FromMilliseconds(200));

                if (match.Success)
                {
                    var fieldId = match.Groups[1].Value;
                    var formValue = Request.Form[key];
                    
                    // Convert StringValues to a simple string or array based on count
                    if (formValue.Count == 1)
                    {
                        Data[fieldId] = formValue.ToString();
                    }
                    else if (formValue.Count > 1)
                    {
                        Data[fieldId] = formValue.ToArray();
                    }
                    else
                    {
                        Data[fieldId] = string.Empty;
                    }
                }
            }

            ValidatePage(CurrentPage);
            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Save the current page data to the API
            if (ApplicationId.HasValue && Data.Any())
            {
                try
                {
                    await _applicationResponseService.SaveApplicationResponseAsync(ApplicationId.Value, Data, HttpContext.Session);
                    _logger.LogInformation("Successfully saved response for Application {ApplicationId}, Page {PageId}", 
                        ApplicationId.Value, CurrentPageId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save response for Application {ApplicationId}, Page {PageId}", 
                        ApplicationId.Value, CurrentPageId);
                    // Continue with navigation even if save fails - we can show a warning to user later
                }
            }

            var flatPages = Template.TaskGroups
                .SelectMany(g => g.Tasks).Where(t => t.TaskId == CurrentTask.TaskId)
                .SelectMany(t => t.Pages)
                .OrderBy(p => p.PageOrder)
                .ToList();

            var index = flatPages.FindIndex(p => p.PageId == CurrentPage.PageId);
            if (index >= 0 && index < CurrentTask.Pages.Count - 1)
            {
                var next = flatPages[index + 1];
                return RedirectToPage(new { pageId = next.PageId });
            }

            return Redirect($"~/render-form/{ReferenceNumber}");
        }

        public async Task<IActionResult> OnGetAutocompleteAsync(string endpoint, string query)
        {
            _logger.LogInformation("Autocomplete search called with endpoint: {Endpoint}, query: {Query}", endpoint, query);
            
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                _logger.LogWarning("Autocomplete search called without endpoint");
                return new JsonResult(new List<object>());
            }

            try
            {
                var results = await _autocompleteService.SearchAsync(endpoint, query);
                _logger.LogInformation("Autocomplete search returned {Count} results", results.Count);
                return new JsonResult(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in autocomplete search endpoint: {Endpoint}, query: {Query}", endpoint, query);
                return new JsonResult(new List<object>());
            }
        }

        private async Task LoadTemplateAsync()
        {
            Template = await _templateProvider.GetTemplateAsync(TemplateId);
        }

        private void InitializeCurrentPage(string pageId)
        {
            var allPages = Template.TaskGroups
                .SelectMany(g => g.Tasks)
                .SelectMany(t => t.Pages)
                .ToList();

            CurrentPage = allPages.FirstOrDefault(p => p.PageId == pageId) ?? allPages.First();

            var pair = Template.TaskGroups
                .SelectMany(g => g.Tasks.Select(t => new { Group = g, Task = t }))
                .First(x => x.Task.Pages.Contains(CurrentPage));

            CurrentGroup = pair.Group;
            CurrentTask = pair.Task;
        }

        private void ValidatePage(Domain.Models.Page page)
        {
            foreach (var field in page.Fields)
            {
                var key = field.FieldId;
                Data.TryGetValue(key, out var rawValue);
                var value = rawValue?.ToString() ?? string.Empty;

                if (field.Validations == null) continue;

                foreach (var rule in field.Validations)
                {
                    // Conditional application
                    if (rule.Condition != null)
                    {
                        Data.TryGetValue(rule.Condition.TriggerField, out var condRaw);
                        var condVal = condRaw?.ToString();
                        var expected = rule.Condition.Value?.ToString();
                        if (rule.Condition.Operator == "equals" && condVal != expected)
                            continue;
                    }

                    switch (rule.Type)
                    {
                        case "required":
                            if (string.IsNullOrWhiteSpace(value))
                                ModelState.AddModelError(key, rule.Message);
                            break;
                        case "regex":
                            if (!Regex.IsMatch(value, rule.Rule.ToString(), RegexOptions.None, TimeSpan.FromMilliseconds(200)) && !String.IsNullOrWhiteSpace(value))
                                ModelState.AddModelError(key, rule.Message);
                            break;
                        case "maxLength":
                            if (value.Length > int.Parse(rule.Rule.ToString()))
                                ModelState.AddModelError(key, rule.Message);
                            break;
                    }
                }
            }
        }

        private void CheckAndClearSessionForNewApplication()
        {
            // Check if we're working with a different application than what's stored in session
            var sessionApplicationId = HttpContext.Session.GetString("CurrentAccumulatedApplicationId");
            var currentApplicationId = ApplicationId?.ToString();
            
            if (!string.IsNullOrEmpty(sessionApplicationId) && 
                sessionApplicationId != currentApplicationId)
            {
                // Clear accumulated data for the previous application
                _applicationResponseService.ClearAccumulatedFormData(HttpContext.Session);
                _logger.LogInformation("Cleared accumulated form data for previous application {PreviousApplicationId}, now working with {CurrentApplicationId}", 
                    sessionApplicationId, currentApplicationId);
            }
            
            // Store the current application ID for future reference
            if (ApplicationId.HasValue)
            {
                HttpContext.Session.SetString("CurrentAccumulatedApplicationId", ApplicationId.Value.ToString());
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

        private void LoadAccumulatedDataFromSession()
        {
            // Get accumulated form data from session and populate the Data dictionary
            var accumulatedData = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
            
            if (accumulatedData.Any())
            {
                // Populate the Data dictionary with accumulated data
                foreach (var kvp in accumulatedData)
                {
                    Data[kvp.Key] = kvp.Value;
                }
                
                _logger.LogInformation("Loaded {Count} accumulated form data entries from session", accumulatedData.Count);
            }
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
            
            // Get all form data accumulated so far
            var accumulatedData = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
            
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
                accumulatedData.ContainsKey(fieldId) && 
                !string.IsNullOrWhiteSpace(accumulatedData[fieldId]?.ToString()));
            
            if (hasAnyFieldCompleted)
            {
                return Domain.Models.TaskStatus.InProgress;
            }
            
            return Domain.Models.TaskStatus.NotStarted;
        }

        /// <summary>
        /// Calculate overall application status based on task statuses
        /// </summary>
        public string CalculateApplicationStatus()
        {
            if (Template?.TaskGroups == null)
            {
                return "InProgress";
            }
            
            var allTasks = Template.TaskGroups.SelectMany(g => g.Tasks).ToList();
            
            // If any task is in progress or completed, application is in progress
            var hasAnyTaskWithProgress = allTasks.Any(task => 
            {
                var status = CalculateTaskStatus(task.TaskId);
                return status == Domain.Models.TaskStatus.InProgress || status == Domain.Models.TaskStatus.Completed;
            });
            
            return hasAnyTaskWithProgress ? "InProgress" : "InProgress"; // Always InProgress until submitted
        }

        public string GetTaskStatusDisplayClass(Domain.Models.TaskStatus status)
        {
            return status switch
            {
                Domain.Models.TaskStatus.Completed => "govuk-tag--green",
                Domain.Models.TaskStatus.InProgress => "govuk-tag--blue",
                Domain.Models.TaskStatus.NotStarted => "govuk-tag--grey",
                Domain.Models.TaskStatus.CannotStartYet => "govuk-tag--orange",
                _ => "govuk-tag--grey"
            };
        }

        public string GetTaskStatusDisplayText(Domain.Models.TaskStatus status)
        {
            return status switch
            {
                Domain.Models.TaskStatus.Completed => "Completed",
                Domain.Models.TaskStatus.InProgress => "In Progress",
                Domain.Models.TaskStatus.NotStarted => "Not Started",
                Domain.Models.TaskStatus.CannotStartYet => "Cannot Start Yet",
                _ => "Not Started"
            };
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
            
            return allTasks.Any(); // Return true only if there are tasks and all are completed
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
    }
}







