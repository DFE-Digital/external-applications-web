using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Pages.Shared;
using DfE.ExternalApplications.Web.Services;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Task = System.Threading.Tasks.Task;
using DfE.ExternalApplications.Infrastructure.Services;
using System.Text.Json;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using DfE.ExternalApplications.Web.Interfaces;

namespace DfE.ExternalApplications.Web.Pages.FormEngine
{
    [ExcludeFromCodeCoverage]
    public class RenderFormModel(
        IFieldRendererService renderer,
        IApplicationResponseService applicationResponseService,
        IFieldFormattingService fieldFormattingService,
        ITemplateManagementService templateManagementService,
        IApplicationStateService applicationStateService,
        IFormStateManager formStateManager,
        IFormNavigationService formNavigationService,
        IFormDataManager formDataManager,
        IFormValidationOrchestrator formValidationOrchestrator,
        IFormConfigurationService formConfigurationService,
        IAutocompleteService autocompleteService,
        IFileUploadService fileUploadService,
        IApplicationsClient applicationsClient,
        IConditionalLogicOrchestrator conditionalLogicOrchestrator,
        INotificationsClient notificationsClient,
        IFormErrorStore formErrorStore,
        IComplexFieldConfigurationService complexFieldConfigurationService,
        IDerivedCollectionFlowService derivedCollectionFlowService,
        IFieldRequirementService fieldRequirementService,
        ILogger<RenderFormModel> logger,
        INavigationHistoryService navigationHistoryService)
        : BaseFormEngineModel(renderer, applicationResponseService, fieldFormattingService, templateManagementService,
            applicationStateService, formStateManager, formNavigationService, formDataManager, formValidationOrchestrator, formConfigurationService, logger)
    {
        private readonly IApplicationsClient _applicationsClient = applicationsClient;
        private readonly IConditionalLogicOrchestrator _conditionalLogicOrchestrator = conditionalLogicOrchestrator;
        private readonly INotificationsClient _notificationsClient = notificationsClient;
        private readonly IFormErrorStore _formErrorStore = formErrorStore;
        private readonly IComplexFieldConfigurationService _complexFieldConfigurationService = complexFieldConfigurationService;
        private readonly IDerivedCollectionFlowService _derivedCollectionFlowService = derivedCollectionFlowService;
        private readonly IFieldRequirementService _fieldRequirementService = fieldRequirementService;
        private readonly INavigationHistoryService _navigationHistoryService = navigationHistoryService;

        [BindProperty(SupportsGet = false)] public Dictionary<string, object> Data { get; set; } = new();

        public string BackLinkUrl => GetBackLinkUrl();

        [BindProperty] public bool IsTaskCompleted { get; set; }
        
        // Collection flow properties from form submission
        [BindProperty] public new string? FlowId { get; set; }
        [BindProperty] public new string? InstanceId { get; set; }
        [BindProperty] public string? FlowPageId { get; set; }
        
        // Derived collection flow properties
        [BindProperty] public string? DerivedFlowId { get; set; }
        [BindProperty] public string? DerivedItemId { get; set; }
        [BindProperty] public string? DerivedPageId { get; set; }
        
        // Calculate IsCollectionFlow automatically based on FlowId and InstanceId presence
        private bool IsCollectionFlow => !string.IsNullOrEmpty(FlowId) && !string.IsNullOrEmpty(InstanceId);
        
        // Calculate IsDerivedFlow automatically based on DerivedFlowId and DerivedItemId presence
        private bool IsDerivedFlow => !string.IsNullOrEmpty(DerivedFlowId) && !string.IsNullOrEmpty(DerivedItemId);

        // Success message for collection operations
        [TempData] public string? SuccessMessage { get; set; }
        
        // Error message for upload operations
        [TempData] public string? ErrorMessage { get; set; }
        
        // Files property for upload field (matches original UploadFile.cshtml.cs)
        public IReadOnlyList<UploadDto> Files { get; set; } = new List<UploadDto>();

        // Conditional logic state for the current form
        public FormConditionalState? ConditionalState { get; set; }

        public async Task OnGetAsync()
        {
                
                
                try
                {
                    await CommonFormEngineInitializationAsync();
                    
                }
                catch (Exception ex)
                {
                _logger.LogError(ex, "Error in CommonFormEngineInitializationAsync for ReferenceNumber: {ReferenceNumber}", ReferenceNumber);
                    throw;
                }

                // Ensure Template is not null to prevent NullReferenceException
                if (Template == null)
                {
                    _logger.LogError("Template is null after CommonFormEngineInitializationAsync for ReferenceNumber: {ReferenceNumber}", ReferenceNumber);
                    Template = new FormTemplate
                    {
                        TemplateId = "dummy",
                        TemplateName = "dummy",
                        Description = "dummy",
                        TaskGroups = new List<TaskGroup>()
                    };
                }
                else
                {
                    
                }

                // Check if this is a preview request
                if (Request.Query.ContainsKey("preview"))
                {
                    // Override the form state for preview requests
                    CurrentFormState = FormState.ApplicationPreview;
                    CurrentGroup = null;
                    CurrentTask = null;
                    CurrentPage = null;
                    
                    // Clear all validation errors for preview since we don't need validation on preview page
                    ModelState.Clear();
                }
                else
                {
                    // Detect sub-flow route segments inside pageId via route value parsing if needed in future
                    // If application is not editable and trying to access a specific page, redirect to preview
                    if (!IsApplicationEditable() && !string.IsNullOrEmpty(CurrentPageId))
                    {
                        Response.Redirect($"~/applications/{ReferenceNumber}");
                        return;
                    }

                    if (!string.IsNullOrEmpty(CurrentPageId))
                    {
                        if (TryParseFlowRoute(CurrentPageId, out var flowId, out var instanceId, out var flowPageId))
                        {
                            // Sub-flow: initialize task and resolve page from task's pages
                            var (group, task) = InitializeCurrentTask(TaskId);
                            CurrentGroup = group;
                            CurrentTask = task;

                            // Find the correct flow and its pages
                            var flowPages = GetFlowPages(task, flowId);
                            if (flowPages != null)
                            {
                                var page = string.IsNullOrEmpty(flowPageId) ? flowPages.FirstOrDefault() : flowPages.FirstOrDefault(p => p.PageId == flowPageId);
                                if (page != null)
                                {
                                    CurrentPage = page;
                                    CurrentFormState = FormState.FormPage; // Render as a normal page
                                    
                                    // If editing existing item, load its data into form fields
                                    // This must happen AFTER LoadAccumulatedDataFromSession is skipped for sub-flows
                                    LoadExistingFlowItemData(flowId, instanceId);
                                    
                                    // Also load any in-progress data for this specific flow instance
                                // IMPORTANT: Progress data takes priority over existing item data as it contains the latest user changes
                                    var progressData = LoadFlowProgress(flowId, instanceId);
                                    foreach (var kvp in progressData)
                                    {
                                    Data[kvp.Key] = kvp.Value; // Always overwrite with progress data (latest changes)
                                }
                                

                                


                            }
                        }
                        }
                        else if (TryParseDerivedFlowRoute(CurrentPageId, out var derivedFlowId, out var derivedItemId, out var derivedPageId))
                        {
                            // Derived flow: initialize task and resolve page from derived flow configuration
                            var (group, task) = InitializeCurrentTask(TaskId);
                            CurrentGroup = group;
                            CurrentTask = task;

                            // Set derived flow properties
                            DerivedFlowId = derivedFlowId;
                            DerivedItemId = derivedItemId;
                            DerivedPageId = derivedPageId;

                            // Find the derived flow configuration
                            var derivedConfig = GetDerivedFlowConfiguration(task, derivedFlowId);
                            if (derivedConfig != null)
                            {
                                // Get the page to render (default to first page if no specific page)
                                var page = string.IsNullOrEmpty(derivedPageId) ? derivedConfig.Pages.FirstOrDefault() : derivedConfig.Pages.FirstOrDefault(p => p.PageId == derivedPageId);
                                if (page != null)
                                {
                                    CurrentPage = page;
                                    CurrentFormState = FormState.FormPage;
                                    
                                    // Load pre-filled data for this derived item
                                    LoadDerivedItemData(derivedConfig, derivedItemId);

                                    // Replace placeholders in page metadata with the item's display name
                                    var displayName = GetDerivedItemDisplayName(derivedConfig, derivedItemId);
                                    if (!string.IsNullOrEmpty(CurrentPage.Title))
                                    {
                                        CurrentPage.Title = CurrentPage.Title
                                            .Replace("{displayName}", displayName)
                                            .Replace("{name}", displayName);
                                    }
                                    if (!string.IsNullOrEmpty(CurrentPage.Description))
                                    {
                                        CurrentPage.Description = CurrentPage.Description
                                            .Replace("{displayName}", displayName)
                                            .Replace("{name}", displayName);
                                    }
                                }
                            }
                        }
                        else
                        {
                            var (group, task, page) = InitializeCurrentPage(CurrentPageId);
                            CurrentGroup = group;
                            CurrentTask = task;
                            CurrentPage = page;
                        }
                    }
                    else if (!string.IsNullOrEmpty(TaskId))
                    {
                        var (group, task) = InitializeCurrentTask(TaskId);
                        CurrentGroup = group;
                        CurrentTask = task;
                        CurrentPage = null; // No specific page for task summary

                        // If task requests collectionFlow summary, switch state accordingly
                        if (_formStateManager.ShouldShowCollectionFlowSummary(CurrentTask))
                        {
                            CurrentFormState = FormState.TaskSummary; // view chooses partial
                        }
                        // If task requests derivedCollectionFlow summary, switch state accordingly
                        else if (_formStateManager.ShouldShowDerivedCollectionFlowSummary(CurrentTask))
                        {
                            CurrentFormState = FormState.DerivedCollectionFlowSummary;
                        }
                    }
                }

                // Check if we need to clear session data for a new application
                CheckAndClearSessionForNewApplication();

            if (string.IsNullOrEmpty(CurrentPageId) || !CurrentPageId.Contains("flow/"))
            {
                await LoadAccumulatedDataFromSessionAsync();
                await ApplyConditionalLogicAsync();
                ModelState.Clear();
                RestoreFormErrors();
                
                ViewData["ValidationErrors"] = ModelState.Where(m => m.Value.Errors.Any())
                    .ToDictionary(m => m.Key, m => m.Value.Errors.Select(e => e.ErrorMessage).ToList());
            }
            else
            {
                await ApplyConditionalLogicAsync();
                ModelState.Clear();
                RestoreFormErrors();
                
                ViewData["ValidationErrors"] = ModelState.Where(m => m.Value.Errors.Any())
                    .ToDictionary(m => m.Key, m => m.Value.Errors.Select(e => e.ErrorMessage).ToList());
            }

                // Initialize task completion status for summaries (standard or derived)
                if (CurrentTask != null)
                {
                    var isSummary = CurrentFormState == FormState.TaskSummary 
                        || _formStateManager.ShouldShowDerivedCollectionFlowSummary(CurrentTask);
                    if (isSummary)
                    {
                        var taskStatus = GetTaskStatusFromSession(CurrentTask.TaskId);
                        IsTaskCompleted = taskStatus == Domain.Models.TaskStatus.Completed;
                        
                        // Clear any validation errors when viewing task summary on GET
                        // Task completion validation errors should only appear after POST, not on initial load
                        ModelState.Clear();
                    }
                }
            // If this GET was reached via back navigation, pop history entry for the current scope
            try
            {
                if (Request.Query.ContainsKey("nav") && string.Equals(Request.Query["nav"], "back", StringComparison.OrdinalIgnoreCase))
                {
                    var scope = BuildHistoryScope(ReferenceNumber, TaskId, CurrentPageId);
                    _navigationHistoryService.Pop(scope, HttpContext.Session);
                }
            }
            catch { }
        }

        public static string BuildHistoryScope(string referenceNumber, string taskId, string currentPageId)
        {
            if (string.IsNullOrEmpty(currentPageId))
            {
                return $"{referenceNumber}:{taskId}";
            }
            var parts = currentPageId.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && string.Equals(parts[0], "flow", StringComparison.OrdinalIgnoreCase))
            {
                var flowId = parts[1];
                var instanceId = parts[2];
                return $"{referenceNumber}:{taskId}:flow:{flowId}:{instanceId}";
            }
            return $"{referenceNumber}:{taskId}";
        }

        public async Task<IActionResult> OnPostTaskSummaryAsync()
        {
            await CommonFormEngineInitializationAsync();

            // Initialize the current task for task summary
            if (!string.IsNullOrEmpty(TaskId))
            {
                var (group, task) = InitializeCurrentTask(TaskId);
                CurrentGroup = group;
                CurrentTask = task;
                CurrentPage = null;
            }

            // Task summary POST does not submit form field data, so Data is empty.
            // We need to apply conditional logic using FormData (session data) for accurate validation.
            // Create a custom conditional logic evaluation using FormData instead of Data.
            try
            {
                if (Template?.ConditionalLogic != null && Template.ConditionalLogic.Any())
                {
                    var context = new ConditionalLogicContext
                    {
                        CurrentPageId = CurrentPageId,
                        CurrentTaskId = TaskId,
                        IsClientSide = false,
                        Trigger = "task_summary_validation"
                    };

                    // CRITICAL FIX: Use FormData (session data) instead of Data (empty on task summary POST)
                    ConditionalState = await _conditionalLogicOrchestrator.ApplyConditionalLogicAsync(Template, FormData, context);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying conditional logic in task summary validation");
                // Continue with empty conditional state - better than failing
                ConditionalState = new FormConditionalState();
            }
            
            // Log conditional state for debugging
            _logger.LogInformation("ConditionalState after ApplyConditionalLogicAsync: Fields={FieldCount}, HiddenFields={HiddenFields}", 
                ConditionalState?.FieldVisibility?.Count ?? 0,
                string.Join(", ", ConditionalState?.FieldVisibility?.Where(kv => !kv.Value).Select(kv => kv.Key) ?? new List<string>()));

            // Handle task completion checkbox state
            if (CurrentTask != null && ApplicationId.HasValue)
            {
                if (IsTaskCompleted)
                {
                    var missingFields = _fieldRequirementService.GetMissingRequiredFields(CurrentTask, Template, FormData, IsFieldHidden);
                    var errorLines = new List<string>();

                    if (missingFields.Any())
                    {
                        foreach (var fieldId in missingFields)
                        {
                            var field = GetFieldFromTask(CurrentTask, fieldId);
                            var fieldLabel = field?.Label?.Value ?? fieldId;
                            errorLines.Add($"• {fieldLabel}");
                        }
                    }

                    // Additional validation for multi-collection flow tasks
                    if (CurrentTask.Summary?.Mode?.Equals("multiCollectionFlow", StringComparison.OrdinalIgnoreCase) == true &&
                        CurrentTask.Summary.Flows != null && CurrentTask.Summary.Flows.Any())
                    {
                        foreach (var flow in CurrentTask.Summary.Flows)
                        {
                            var items = ReadCollectionItemsFromFormData(flow.FieldId);
                            var itemCount = items.Count;

                            var requiredMin = flow.MinItems ?? 1; // default to at least one item
                            if (itemCount < requiredMin)
                            {
                                var flowTitle = string.IsNullOrWhiteSpace(flow.Title)
                                    ? (string.IsNullOrWhiteSpace(CurrentTask?.TaskName) ? "this section" : CurrentTask!.TaskName)
                                    : flow.Title;
                                errorLines.Add($"• Add at least {requiredMin} item(s) to {flowTitle}");
                                _logger.LogInformation("Collection flow '{FlowId}' requires at least {MinItems} items but has {Count}", flow.FlowId, requiredMin, itemCount);
                            }

                        }
                    }

                    if (errorLines.Any())
                    {
                        // Cannot complete task - required fields are missing
                        ModelState.Clear();

                        // Create error message with bullet points
                        var errorMessage = "You cannot mark this section as complete because some required questions have not been answered:\n" +
                                         string.Join("\n", errorLines);
                        
                        ModelState.AddModelError(string.Empty, errorMessage);
                        
                        IsTaskCompleted = false; // Reset the checkbox state
                        
                        // CRITICAL: Set CurrentFormState so the view knows to render the task summary
                        CurrentFormState = FormState.TaskSummary;
                        
                        // DON'T save ModelState errors to FormErrorStore - they should only appear once
                        // on this immediate response, not persist to next GET request
                        return Page();
                    }
                    
                    // Mark the task as completed in session and API
                    await _applicationStateService.SaveTaskStatusAsync(ApplicationId.Value, CurrentTask.TaskId, Domain.Models.TaskStatus.Completed, HttpContext.Session);
                }
                else
                {
                    // Task was unchecked - set it back to in progress if it has data, otherwise not started
                    var currentStatus = _applicationStateService.CalculateTaskStatus(CurrentTask.TaskId, Template, FormData, ApplicationId, HttpContext.Session, ApplicationStatus);
                    if (currentStatus == Domain.Models.TaskStatus.Completed)
                    {
                        // Only override if it was explicitly marked as completed - revert to calculated status
                        var calculatedStatus = HasAnyTaskData(CurrentTask) ? Domain.Models.TaskStatus.InProgress : Domain.Models.TaskStatus.NotStarted;
                        await _applicationStateService.SaveTaskStatusAsync(ApplicationId.Value, CurrentTask.TaskId, calculatedStatus, HttpContext.Session);
                    }
                }
            }

            // Redirect to the task list page
            return Redirect($"/applications/{ReferenceNumber}");
        }

        public async Task<IActionResult> OnPostSubmitApplicationAsync()
        {
            // Clear any model state errors for route parameters since they're not relevant for preview submission
            ModelState.Remove(nameof(TaskId));
            ModelState.Remove(nameof(CurrentPageId));
            ModelState.Remove("TaskId");
            ModelState.Remove("CurrentPageId");
            ModelState.Remove("pageId");
            ModelState.Remove("taskId");
            
            // Initialize common form engine data first (loads Template, FormData, etc.)
            await CommonFormEngineInitializationAsync();

            // Prevent submission if application is not editable
            if (!IsApplicationEditable())
            {
                return RedirectToPage("/FormEngine/RenderForm", new { referenceNumber = ReferenceNumber });
            }

            // Check if all tasks are completed before allowing submission
            if (!AreAllTasksCompleted())
            {
                _logger.LogWarning("Cannot submit application {ReferenceNumber} - not all tasks completed", ReferenceNumber);
                
                // Override the form state for preview with errors
                CurrentFormState = FormState.ApplicationPreview;
                
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
                
                return RedirectToPage("/Applications/ApplicationSubmitted", new { referenceNumber = ReferenceNumber });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to submit application {ApplicationId} with reference {ReferenceNumber}", 
                    ApplicationId.Value, ReferenceNumber);
                
                ModelState.AddModelError("", $"An error occurred while submitting your application: {ex.Message}. Please try again.");
                return Page();
            }
        }

        public async Task<IActionResult> OnPostPageAsync()
        {
            _logger.LogInformation("POST: OnPostPageAsync called - ReferenceNumber='{ReferenceNumber}', TaskId='{TaskId}', CurrentPageId='{CurrentPageId}'", 
                ReferenceNumber, TaskId, CurrentPageId);
            _logger.LogInformation("POST: Request URL: {Url}", $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}");
            _logger.LogInformation("POST: Form data keys: {Keys}", string.Join(", ", Request.Form.Keys));
            
            // This handler is also used by task summary pages which do not post a pageId.
            // Non-nullable reference types are implicitly required in MVC, so clear any implicit
            // model state error for missing pageId to avoid short-circuiting to Page().
            ModelState.Remove(nameof(CurrentPageId));
            ModelState.Remove("pageId");
            
            // Check if this is a confirmed action coming back from confirmation page
            if (Request.Query.ContainsKey("confirmed") && Request.Query["confirmed"] == "true")
            {
                // Restore the original form data from TempData
                var confirmedDataJson = TempData["ConfirmedFormData"]?.ToString();
                var confirmedHandler = TempData["ConfirmedHandler"]?.ToString();
                
                if (!string.IsNullOrEmpty(confirmedDataJson))
                {
                    try
                    {
                        var confirmedData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(confirmedDataJson);
                        if (confirmedData != null)
                        {
                            // Merge confirmed data into current Data
                            foreach (var kvp in confirmedData)
                            {
                                Data[kvp.Key] = kvp.Value;
                            }
                            _logger.LogInformation("Restored {Count} confirmed form fields for handler {Handler}", 
                                confirmedData.Count, confirmedHandler);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to deserialize confirmed form data");
                    }
                }
            }

            await CommonFormEngineInitializationAsync();

            // Prevent editing if application is not editable
            if (!IsApplicationEditable())
            {
                return RedirectToPage("/FormEngine/RenderForm", new { referenceNumber = ReferenceNumber });
            }


            
            // URL decode the pageId to handle encoded forward slashes from form submissions
            if (!string.IsNullOrEmpty(CurrentPageId))
            {
                CurrentPageId = System.Web.HttpUtility.UrlDecode(CurrentPageId);

            }

            if (!string.IsNullOrEmpty(CurrentPageId))
            {
                if (TryParseFlowRoute(CurrentPageId, out var flowId, out var instanceId, out var flowPageId))
                {
                    

                    var (group, task) = InitializeCurrentTask(TaskId);
                    CurrentGroup = group;
                    CurrentTask = task;

                    // Find the correct flow and its pages
                    var flowPages = GetFlowPages(task, flowId);
                    if (flowPages != null)
                    {
                        var page = string.IsNullOrEmpty(flowPageId) ? flowPages.FirstOrDefault() : flowPages.FirstOrDefault(p => p.PageId == flowPageId);
                        if (page != null)
                        {
                            CurrentPage = page;
                        }
                    }
                }
                else
                {
            var (group, task, page) = InitializeCurrentPage(CurrentPageId);
            CurrentGroup = group;
            CurrentTask = task;
            CurrentPage = page;
                }
            }
            else if (!string.IsNullOrEmpty(TaskId))
            {
                // No pageId posted (e.g., task summary/derived summary). Initialize the task context.
                var (group, task) = InitializeCurrentTask(TaskId);
                CurrentGroup = group;
                CurrentTask = task;
                CurrentPage = null;
                _logger.LogInformation("POST: Initialized CurrentTask '{TaskId}' for summary POST (no pageId)", CurrentTask?.TaskId);
            }
            else if (!string.IsNullOrEmpty(TaskId))
            {
                var (group, task) = InitializeCurrentTask(TaskId);
                CurrentGroup = group;
                CurrentTask = task;
                CurrentPage = null; // No specific page for task summary
            }

            // Removed verbose debug logging of posted keys

            // Collect date parts for fields rendered with GOV.UK date input
            var dateParts = new Dictionary<string, (string? Day, string? Month, string? Year)>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in Request.Form.Keys)
            {
                var match = Regex.Match(key, @"^Data\[(.+?)\]$", RegexOptions.None, TimeSpan.FromMilliseconds(200));

                if (match.Success)
                {
                    var fieldId = match.Groups[1].Value;
                    // Normalise autocomplete ids like Data_trustsSearch to trustsSearch
                    var normalisedFieldId = fieldId.StartsWith("Data_", StringComparison.Ordinal) ? fieldId.Substring(5) : fieldId;
                    var formValue = Request.Form[key];

                    _logger.LogInformation("DEBUG: Processing form field - Key: '{Key}', FieldId: '{FieldId}', FormValue: '{FormValue}'", 
                        key, fieldId, formValue.ToString());

                    // Convert StringValues to a simple string or array based on count
                    if (formValue.Count == 1)
                    {
                        var val = formValue.ToString();
                        Data[fieldId] = val;
                        if (!string.Equals(fieldId, normalisedFieldId, StringComparison.Ordinal))
                        {
                            Data[normalisedFieldId] = val;
                        }
                        _logger.LogInformation("DEBUG: Added to Data - FieldId: '{FieldId}', Value: '{Value}'", fieldId, val);
                    }
                    else if (formValue.Count > 1)
                    {
                        var arr = formValue.ToArray();
                        Data[fieldId] = arr;
                        if (!string.Equals(fieldId, normalisedFieldId, StringComparison.Ordinal))
                        {
                            Data[normalisedFieldId] = arr;
                        }
                    }
                    else
                    {
                        Data[fieldId] = string.Empty;
                        if (!string.Equals(fieldId, normalisedFieldId, StringComparison.Ordinal))
                        {
                            Data[normalisedFieldId] = string.Empty;
                        }
                    }
                }
                else
                {
                    // Match date inputs like Data[fieldId].Day / Data[fieldId]-day (support both dot and hyphen)
                    var dateMatch = Regex.Match(key, @"^Data\[(.+?)\](?:[.\-](day|month|year))$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
                    if (dateMatch.Success)
                    {
                        var fieldId = dateMatch.Groups[1].Value;
                        var part = dateMatch.Groups[2].Value.ToLowerInvariant();
                        var formValue = Request.Form[key].ToString();

                        if (!dateParts.TryGetValue(fieldId, out var parts))
                        {
                            parts = (null, null, null);
                        }

                        switch (part)
                        {
                            case "day":
                                parts.Day = formValue;
                                break;
                            case "month":
                                parts.Month = formValue;
                                break;
                            case "year":
                                parts.Year = formValue;
                                break;
                        }

                        dateParts[fieldId] = parts;
                    }
                }
              }

            // Apply conditional logic after processing form data changes


            

            
            // Handle upload fields that use session data instead of form data to avoid truncation
            if (IsCollectionFlow)
            {
                var flowProgress = LoadFlowProgress(FlowId, InstanceId);
                foreach (var key in Data.Keys.ToList())
                {
                    if (Data[key]?.ToString() == "UPLOAD_FIELD_SESSION_DATA")
                    {
                        // Replace with actual data from session
                        if (flowProgress.TryGetValue(key, out var sessionValue))
                        {
                            Data[key] = sessionValue;

                        }
                    }
                }
            }
            else
            {
                // For regular (non-collection) forms, also replace upload placeholders with session data
                var accumulatedData = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
                foreach (var key in Data.Keys.ToList())
                {
                    if (Data[key]?.ToString() == "UPLOAD_FIELD_SESSION_DATA")
                    {
                        // Replace with actual data from session
                        if (accumulatedData.TryGetValue(key, out var sessionValue))
                        {
                            Data[key] = sessionValue;
                            _logger.LogInformation("Replaced upload placeholder for field {FieldId} with session data", key);
                        }
                        else
                        {
                            // No session data means no files uploaded yet - keep placeholder so validation can detect it
                            _logger.LogInformation("No session data found for upload field {FieldId} - validation will detect empty field", key);
                        }
                    }
                }
            }
            
            await ApplyConditionalLogicAsync("change");

            // Compose collected date parts into a single ISO date string so summaries recognise an answer
            if (dateParts.Count > 0)
            {
                foreach (var kvp in dateParts)
                {
                    var fieldId = kvp.Key;
                    var parts = kvp.Value;
                    var anyEntered = !string.IsNullOrWhiteSpace(parts.Day) || !string.IsNullOrWhiteSpace(parts.Month) || !string.IsNullOrWhiteSpace(parts.Year);

                    if (!anyEntered)
                    {
                        continue;
                    }

                    if (int.TryParse(parts.Year, out var y) && int.TryParse(parts.Month, out var m) && int.TryParse(parts.Day, out var d))
                    {
                        try
                        {
                            var dt = new DateTime(y, m, d);
                            var iso = dt.ToString("yyyy-MM-dd");
                            var normalisedFieldId = fieldId.StartsWith("Data_", StringComparison.Ordinal) ? fieldId.Substring(5) : fieldId;
                            Data[fieldId] = iso;
                            if (!string.Equals(fieldId, normalisedFieldId, StringComparison.Ordinal)) Data[normalisedFieldId] = iso;
                        }
                        catch
                        {
                            // Invalid date combo: set a joined value so validator can produce a message and retain the parts
                            var joined = $"{parts.Year}-{parts.Month}-{parts.Day}";
                            var normalisedFieldId = fieldId.StartsWith("Data_", StringComparison.Ordinal) ? fieldId.Substring(5) : fieldId;
                            Data[fieldId] = joined;
                            if (!string.Equals(fieldId, normalisedFieldId, StringComparison.Ordinal)) Data[normalisedFieldId] = joined;
                        }
                    }
                    else
                    {
                        // Partial or non-numeric: set a joined value so validator can produce a message
                        var joined = $"{parts.Year}-{parts.Month}-{parts.Day}";
                        var normalisedFieldId = fieldId.StartsWith("Data_", StringComparison.Ordinal) ? fieldId.Substring(5) : fieldId;
                        Data[fieldId] = joined;
                        if (!string.Equals(fieldId, normalisedFieldId, StringComparison.Ordinal)) Data[normalisedFieldId] = joined;
                    }
                }
            }

			bool isDerivedFlowRoute = TryParseDerivedFlowRoute(CurrentPageId, out var _, out var _, out var _);
			if (!isDerivedFlowRoute && CurrentPage != null)
			{
				ValidateCurrentPage(CurrentPage, Data);
			}

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("ModelState invalid on POST Page");

                // (Reverted) Do not accumulate general invalid form data to session; sub-flow persistence below is sufficient
                
                // For sub-flow pages, persist latest values to flow progress prior to redirect
                try
                {
                    if (TryParseFlowRoute(CurrentPageId, out var fId, out var instId, out _))
                    {
                        SaveFlowProgress(fId, instId, Data);
                        _logger.LogInformation("Saved in-progress flow data for flow {FlowId}, instance {InstanceId} with {Count} fields due to validation errors.", fId, instId, Data?.Count ?? 0);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save flow progress on validation failure.");
                }

                var contextKey = GetFormErrorContextKey();
                _formErrorStore.Save(contextKey, ModelState);
                
                if (TryParseDerivedFlowRoute(CurrentPageId, out _, out _, out _))
                {
                    var selfUrl = $"/applications/{ReferenceNumber}/{TaskId}/{CurrentPageId}";
                    return Redirect(selfUrl);
                }

                if (TryParseFlowRoute(CurrentPageId, out _, out _, out _))
                {
                    var selfUrl = $"/applications/{ReferenceNumber}/{TaskId}/{CurrentPageId}";
                    return Redirect(selfUrl);
                }
                
                return Page();
            }


            // When AllowMultiple is true for an autocomplete complex field, append new selection
            // to any existing array value instead of replacing it
            if (CurrentPage != null)
            {
                try
                {
                    foreach (var field in CurrentPage.Fields.Where(f => f.Type == "complexField" && f.ComplexField != null))
                    {
                        var cfg = _complexFieldConfigurationService.GetConfiguration(field.ComplexField.Id);
                        if (!string.Equals(cfg.FieldType, "autocomplete", StringComparison.OrdinalIgnoreCase) || !cfg.AllowMultiple)
                        {
                            continue;
                        }

                        var key = field.FieldId;
                        if (!Data.TryGetValue(key, out var newValObj))
                        {
                            continue;
                        }

                        var newVal = newValObj?.ToString();
                        if (string.IsNullOrWhiteSpace(newVal))
                        {
                            continue;
                        }

                        // Load existing selections from accumulated session
                        var acc = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
                        var list = new List<object>();
                        if (acc.TryGetValue(key, out var existing) && !string.IsNullOrWhiteSpace(existing?.ToString()))
                        {
                            var existingText = existing!.ToString()!;
                            var addedExisting = false;
                            // Try parse as array of objects
                            try
                            {
                                var parsedArray = JsonSerializer.Deserialize<List<object>>(existingText);
                                if (parsedArray != null)
                                {
                                    list = parsedArray;
                                    addedExisting = true;
                                }
                            }
                            catch { }

                            // If not an array, try parse as single object and add it as first element
                            if (!addedExisting)
                            {
                                try
                                {
                                    using var doc = JsonDocument.Parse(existingText);
                                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                                    {
                                        list.Add(doc.RootElement.Clone());
                                        addedExisting = true;
                                    }
                                }
                                catch { }
                            }

                            // If still not added and it's a non-empty string, include as string element
                            if (!addedExisting && !string.IsNullOrWhiteSpace(existingText))
                            {
                                list.Add(existingText);
                            }
                        }

                        // Avoid duplicates by comparing JSON string
                        bool exists = false;
                        try
                        {
                            var newJson = newVal;
                            exists = list.Any(x => (x?.ToString() ?? "") == newJson);
                        }
                        catch { }

                        if (!exists)
                        {
                            try
                            {
                                using var newDoc = JsonDocument.Parse(newVal);
                                if (newDoc.RootElement.ValueKind == JsonValueKind.Object || newDoc.RootElement.ValueKind == JsonValueKind.Array)
                                {
                                    list.Add(newDoc.RootElement.Clone());
                                }
                                else if (newDoc.RootElement.ValueKind == JsonValueKind.String)
                                {
                                    list.Add(newDoc.RootElement.GetString() ?? string.Empty);
                                }
                                else
                                {
                                    list.Add(newDoc.RootElement.ToString());
                                }
                            }
                            catch
                            {
                                // If not JSON, store as string value
                                list.Add(newVal);
                            }
                        }

                        var updatedJson = JsonSerializer.Serialize(list);
                        // Update both normalized and Data_ forms to be safe
                        Data[key] = updatedJson;
                        Data[$"Data_{key}"] = updatedJson;
                        _applicationResponseService.AccumulateFormData(new Dictionary<string, object> { [key] = updatedJson }, HttpContext.Session);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to merge multi-select autocomplete values");
                }
            }

            // Save the current page data to the API (skip for sub-flows as they accumulate data differently)
            bool isSubFlow = TryParseFlowRoute(CurrentPageId, out _, out _, out _);
            if (ApplicationId.HasValue && Data.Any() && !isSubFlow)
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

            // Before deciding where to go, push current page URL to navigation history so Back returns here
            try
            {
                if (!string.IsNullOrEmpty(CurrentPageId))
                {
                    var scope = RenderFormModel.BuildHistoryScope(ReferenceNumber, TaskId, CurrentPageId);
                    var currentUrl = $"/applications/{ReferenceNumber}/{TaskId}/{CurrentPageId}";
                    _navigationHistoryService.Push(scope, currentUrl, HttpContext.Session);
                }
                else if (!string.IsNullOrEmpty(TaskId))
                {
                    var scope = RenderFormModel.BuildHistoryScope(ReferenceNumber, TaskId, CurrentPageId);
                    var currentUrl = $"/applications/{ReferenceNumber}/{TaskId}";
                    _navigationHistoryService.Push(scope, currentUrl, HttpContext.Session);
                }
            }
            catch { }

            // Use the new navigation logic to determine where to go after saving
            if (CurrentTask != null && CurrentPage != null)
            {
                // If this is a sub-flow route, compute next page within the flow
                if (TryParseFlowRoute(CurrentPageId, out var flowId, out var instanceId, out var flowPageId))
                {
                    // Find the correct flow and its pages
                    var flowPages = GetFlowPages(CurrentTask, flowId);
                    var flowFieldId = GetFlowFieldId(CurrentTask, flowId);
                    
                    if (flowPages != null && !string.IsNullOrEmpty(flowFieldId))
                    {
                        // Persist in-progress sub-flow data for this instance
                        SaveFlowProgress(flowId, instanceId, Data);

                        var index = flowPages.FindIndex(p => p.PageId == CurrentPage.PageId);
                        var isLast = index == -1 || index >= flowPages.Count - 1;
                        if (!isLast)
                        {
                            // Find the next visible page using conditional logic
                            string? nextPageId = null;
                            
                            // Check if we have conditional logic to determine next page
                            if (ConditionalState != null)
                            {
                                _logger.LogDebug("Sub-flow navigation: checking conditional logic for pages. Current page: {CurrentPageId}, Flow: {FlowId}", CurrentPage.PageId, flowId);
                                
                                // CRITICAL FIX: Re-evaluate conditional logic with complete flow data for navigation
                                var mergedData = LoadFlowProgress(FlowId, InstanceId);
                                foreach (var kvp in Data)
                                {
                                    mergedData[kvp.Key] = kvp.Value;
                                }
                                
                                
                                var navContext = new ConditionalLogicContext
                                {
                                    CurrentPageId = CurrentPageId,
                                    CurrentTaskId = TaskId,
                                    IsClientSide = false,
                                    Trigger = "change"
                                };
                                
                                // Re-compute conditional state with complete data
                                var updatedConditionalState = await _conditionalLogicOrchestrator.ApplyConditionalLogicAsync(Template, mergedData, navContext);
                                
                                
                                // Look for the next visible page after current page using updated state
                                for (int i = index + 1; i < flowPages.Count; i++)
                                {
                                    var candidatePage = flowPages[i];
                                    
                                    // Check if this page should be skipped due to conditional logic using updated state
                                    var isHidden = updatedConditionalState.PageVisibility.TryGetValue(candidatePage.PageId, out var isVisible) && !isVisible;
                                    var isSkipped = updatedConditionalState.SkippedPages.Contains(candidatePage.PageId);
                                    
                                    
                                    if (!isHidden && !isSkipped)
                                    {
                                        nextPageId = candidatePage.PageId;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                // Fallback to simple next page logic if no conditional logic
                                nextPageId = flowPages[index + 1].PageId;
                            }
                            
                            
                            if (!string.IsNullOrEmpty(nextPageId))
                            {
                                var nextUrl = _formNavigationService.GetSubFlowPageUrl(CurrentTask.TaskId, ReferenceNumber, flowId, instanceId, nextPageId);
                                return Redirect(nextUrl);
                            }
                            
                            // If no valid next page found, treat as last page and complete the flow
                            // Fall through to flow completion logic below
                        }
                        
                        // Flow completion logic - execute when no next page is found
                        // Flow complete: append item to collection and go back to collection summary
                        if (!string.IsNullOrEmpty(flowFieldId))
                        {
                            // Determine if this is a new item or an update
                            bool isNewItem = !IsExistingCollectionItem(flowFieldId, instanceId);
                            
                            // Merge accumulated progress with final page data
                            var accumulated = LoadFlowProgress(flowId, instanceId);
            
                            foreach (var kv in Data)
                            {
                                // Do not overwrite existing upload data with placeholder token
                                if (kv.Value?.ToString() == "UPLOAD_FIELD_SESSION_DATA" && accumulated.ContainsKey(kv.Key))
                                {
                                    continue;
                                }
                                accumulated[kv.Key] = kv.Value;
                            }

                            AppendCollectionItemToSession(flowPages, flowFieldId, instanceId, accumulated);
                            
                            // Generate success message
                            var flow = CurrentTask.Summary?.Flows?.FirstOrDefault(f => f.FlowId == flowId);
                            if (flow != null)
                            {
                                // Use the accumulated data (all fields from the item)
                                if (isNewItem)
                                {
                                    SuccessMessage = GenerateSuccessMessage(flow.AddItemMessage, "add", accumulated, flow.Title);
                                }
                                else
                                {
                                    SuccessMessage = GenerateSuccessMessage(flow.UpdateItemMessage, "update", accumulated, flow.Title);
                                }
                            }
                            
                            if (ApplicationId.HasValue)
                            {
                                // Trigger save for the collection field
                                var acc = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
                                if (acc.TryGetValue(flowFieldId, out var collectionValue))
                                {
                                    await _applicationResponseService.SaveApplicationResponseAsync(ApplicationId.Value, new Dictionary<string, object> { [flowFieldId] = collectionValue }, HttpContext.Session);
                                }
                            }
                            // Clear the in-progress cache for this instance
                            ClearFlowProgress(flowId, instanceId);

                            // Clear navigation history
                            var scope = BuildHistoryScope(ReferenceNumber, TaskId, CurrentPageId);
                            _navigationHistoryService.Clear(scope, HttpContext.Session);
                        }
                        var backToSummary = _formNavigationService.GetCollectionFlowSummaryUrl(CurrentTask.TaskId, ReferenceNumber);
                        return Redirect(backToSummary);
                    }
                }
                
                _logger.LogInformation("POST: Checking if CurrentPageId '{CurrentPageId}' is a derived flow route", CurrentPageId);
                
                // Handle derived collection flow form submissions
                if (TryParseDerivedFlowRoute(CurrentPageId, out var derivedFlowId, out var derivedItemId, out var derivedPageId))
                {
                    _logger.LogInformation("POST: Detected derived flow route - flowId='{FlowId}', itemId='{ItemId}', pageId='{PageId}'", 
                        derivedFlowId, derivedItemId, derivedPageId);
                }
                else
                {
                    _logger.LogInformation("POST: CurrentPageId '{CurrentPageId}' is NOT a derived flow route", CurrentPageId);
                }
                
			if (TryParseDerivedFlowRoute(CurrentPageId, out derivedFlowId, out derivedItemId, out derivedPageId))
                {
				var correctTask = Template?.TaskGroups?.SelectMany(g => g.Tasks)?.FirstOrDefault(t => t.TaskId == TaskId);
				var derivedConfig = GetDerivedFlowConfiguration(correctTask, derivedFlowId);
				if (derivedConfig != null)
				{
					var currentDerivedPage = string.IsNullOrEmpty(derivedPageId)
						? derivedConfig.Pages?.FirstOrDefault()
						: derivedConfig.Pages?.FirstOrDefault(p => p.PageId == derivedPageId);

					if (currentDerivedPage != null)
					{
						ValidateCurrentPage(currentDerivedPage, Data);
					}

					if (!ModelState.IsValid)
					{
						var contextKey = GetFormErrorContextKey();
						_formErrorStore.Save(contextKey, ModelState);
						var selfUrl = $"/applications/{ReferenceNumber}/{TaskId}/{CurrentPageId}";
						return Redirect(selfUrl);
					}
				}

                    if (derivedConfig != null)
                    {
                    
                        
                        // Save the declaration data and mark as signed
                        _derivedCollectionFlowService.SaveItemDeclaration(
                            derivedConfig.FieldId, 
                            derivedItemId, 
                            Data, 
                            "Signed", 
                            FormData);

                        

                        // Save to API
                        if (ApplicationId.HasValue)
                        {
                            
                            await _applicationResponseService.SaveApplicationResponseAsync(ApplicationId.Value, FormData, HttpContext.Session);
                            
                        }
                        else
                        {
                            _logger.LogWarning("DerivedFlow POST: No ApplicationId found, skipping API save");
                        }

                        // Generate success message
                        var displayName = GetDerivedItemDisplayName(derivedConfig, derivedItemId);
                        var templateMessage = derivedConfig.SignedMessage ?? "Declaration for {displayName} has been signed";
                        SuccessMessage = templateMessage
                            .Replace("{displayName}", displayName)
                            .Replace("{name}", displayName);
                        
                        

                        // Redirect back to derived collection summary
                        var redirectUrl = $"/applications/{ReferenceNumber}/{TaskId}";
                        
                        return Redirect(redirectUrl);
                    }
                    else
                    {
                        _logger.LogError("DerivedFlow POST: Could not find derived config for flowId='{FlowId}'", derivedFlowId);
                    }
                }
                else if (_formStateManager.ShouldShowDerivedCollectionFlowSummary(CurrentTask))
                {
                    // Handle POST from derived collection flow summary page (Continue button)
                    
                    
                    // Handle task completion checkbox and redirect to task list
                    var completedValue = Request.Form["IsTaskCompleted"].ToString();
                    var isCompleted = !string.IsNullOrEmpty(completedValue) &&
                        (string.Equals(completedValue, "true", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(completedValue, "on", StringComparison.OrdinalIgnoreCase));
                    

                    if (isCompleted)
                    {
                        
                        
                        try
                        {
                            // Persist a flag so API has an audit of completion action
                            await _applicationResponseService.SaveApplicationResponseAsync(ApplicationId.Value, new Dictionary<string, object>
                            {
                                [$"{TaskId}_completed"] = true
                            }, HttpContext.Session);
                            
                            // Also set the task status to Completed (matches TaskSummary behaviour)
                            if (CurrentTask != null)
                            {
                                await _applicationStateService.SaveTaskStatusAsync(
                                    ApplicationId.Value,
                                    CurrentTask.TaskId,
                                    Domain.Models.TaskStatus.Completed,
                                    HttpContext.Session);
                                
                            }

                            
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "POST: Error saving task completion status");
                        }
                        
                        // Use RedirectToPage to ensure proper page model initialization
                        _logger.LogInformation("POST: About to redirect to task list using RedirectToPage with ReferenceNumber: {ReferenceNumber}", ReferenceNumber);
                        return RedirectToPage("/FormEngine/RenderForm", new { referenceNumber = ReferenceNumber });
                    }
                    else
                    {
                        

                        // If unchecked: set task status based on calculated state (in progress if any data exists, else not started)
                        if (CurrentTask != null && ApplicationId.HasValue)
                        {
                            var hasAnyData = _applicationStateService.CalculateTaskStatus(CurrentTask.TaskId, Template, FormData, ApplicationId, HttpContext.Session, ApplicationStatus) 
                                != Domain.Models.TaskStatus.NotStarted;
                            var newStatus = hasAnyData ? Domain.Models.TaskStatus.InProgress : Domain.Models.TaskStatus.NotStarted;
                            await _applicationStateService.SaveTaskStatusAsync(ApplicationId.Value, CurrentTask.TaskId, newStatus, HttpContext.Session);
                            
                        }
                        
                        // Use RedirectToPage to ensure proper page model initialization
                        return RedirectToPage("/FormEngine/RenderForm", new { referenceNumber = ReferenceNumber });
                    }
                }
                else
                {
                    // First check if returnToSummaryPage is true and should be respected
                    if (CurrentPage.ReturnToSummaryPage)
                    {


                        
                        // Check if conditional logic suggests a different next page (override returnToSummaryPage)
                        string? conditionalNextPageId = null;
                        bool hasConditionalTrigger = false;
                        
                        if (ConditionalState != null && Template != null)
                        {
                        // FIXED: Check if conditional rules specifically show/reveal new pages, not just any trigger
                        hasConditionalTrigger = HasConditionalLogicShowingPages();

                        _logger.LogInformation("[FLOW DEBUG] ReturnToSummaryPage=true path - hasConditionalTrigger: {HasTrigger}, currentPageId: {PageId}", hasConditionalTrigger, CurrentPage.PageId);
                        
                        if (hasConditionalTrigger)
                        {
                            _logger.LogInformation("[FLOW DEBUG] Data before calling GetNextPageAsync:");
                            foreach (var kv in Data.Take(10))
                            {
                                _logger.LogInformation("[FLOW DEBUG] Data[{Key}] = {Value}", kv.Key, kv.Value?.ToString() ?? "null");
                            }

                            var context = new ConditionalLogicContext
                            {
                                CurrentPageId = CurrentPageId,
                                CurrentTaskId = TaskId,
                                IsClientSide = false,
                                Trigger = "change"
                            };
                            
                            conditionalNextPageId = await _conditionalLogicOrchestrator.GetNextPageAsync(Template, Data, CurrentPage.PageId, context);
                            _logger.LogInformation("[FLOW DEBUG] GetNextPageAsync returned: {NextPageId}", conditionalNextPageId ?? "null");
                        }
                        }
                        
                        // If conditional logic found a next page AND was triggered, navigate there (override returnToSummaryPage)
                        if (hasConditionalTrigger && !string.IsNullOrEmpty(conditionalNextPageId))
                        {
                            var nextUrl = $"/applications/{ReferenceNumber}/{CurrentTask.TaskId}/{conditionalNextPageId}";

                            return Redirect(nextUrl);
                        }
                        
                        // No conditional override - respect returnToSummaryPage
                        var summaryUrl = _formNavigationService.GetTaskSummaryUrl(CurrentTask.TaskId, ReferenceNumber);

                        return Redirect(summaryUrl);
                    }
                    
                    // returnToSummaryPage=false - proceed with normal next page logic
                    string? nextPageId = null;
                    
                    if (ConditionalState != null && Template != null)
                    {
                        _logger.LogInformation("[FLOW DEBUG] ReturnToSummaryPage=false path - currentPageId: {PageId}", CurrentPage.PageId);
                        _logger.LogInformation("[FLOW DEBUG] Data before calling GetNextPageAsync:");
                        foreach (var kv in Data.Take(10))
                        {
                            _logger.LogInformation("[FLOW DEBUG] Data[{Key}] = {Value}", kv.Key, kv.Value?.ToString() ?? "null");
                        }

                        var context = new ConditionalLogicContext
                        {
                            CurrentPageId = CurrentPageId,
                            CurrentTaskId = TaskId,
                            IsClientSide = false,
                            Trigger = "change"
                        };
                        
                        nextPageId = await _conditionalLogicOrchestrator.GetNextPageAsync(Template, Data, CurrentPage.PageId, context);
                        _logger.LogInformation("[FLOW DEBUG] GetNextPageAsync returned: {NextPageId}", nextPageId ?? "null");
                    }
                    
                    // If conditional logic found a next page, navigate to it
                    if (!string.IsNullOrEmpty(nextPageId))
                    {
                        var nextUrl = $"/applications/{ReferenceNumber}/{CurrentTask.TaskId}/{nextPageId}";

                        return Redirect(nextUrl);
                    }
                    
                    // No conditional next page - find the next page in sequence
                    Domain.Models.Page? sequentialNextPage = null;
                    if (CurrentTask.Pages != null && CurrentTask.Pages.Any())
                    {
                        var currentPageIndex = CurrentTask.Pages.FindIndex(p => p.PageId == CurrentPage.PageId);
                        if (currentPageIndex != -1 && currentPageIndex < CurrentTask.Pages.Count - 1)
                        {
                            sequentialNextPage = CurrentTask.Pages[currentPageIndex + 1];
                        }
                    }
                    
                    if (sequentialNextPage != null)
                    {
                        var nextUrl = $"/applications/{ReferenceNumber}/{CurrentTask.TaskId}/{sequentialNextPage.PageId}";

                        return Redirect(nextUrl);
                    }
                    
                    // No next page found - go to task summary as fallback
                    var fallbackUrl = _formNavigationService.GetTaskSummaryUrl(CurrentTask.TaskId, ReferenceNumber);

                    return Redirect(fallbackUrl);
                }
            }
            else if (CurrentTask != null)
            {
                // Fallback: redirect to the appropriate summary/list depending on config
                if (_formStateManager.ShouldShowCollectionFlowSummary(CurrentTask))
                {
                    var url = _formNavigationService.GetCollectionFlowSummaryUrl(CurrentTask.TaskId, ReferenceNumber);
                    return Redirect(url);
                }
                if (_formStateManager.ShouldShowDerivedCollectionFlowSummary(CurrentTask))
                {
                    var completedValue = Request.Form["IsTaskCompleted"].ToString();
                    var isCompleted = !string.IsNullOrEmpty(completedValue) &&
                        (string.Equals(completedValue, "true", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(completedValue, "on", StringComparison.OrdinalIgnoreCase));
                    
                    if (isCompleted)
                    {
                        var derivedFlows = CurrentTask?.Summary?.DerivedFlows;
                        var errorLines = new List<string>();
                        
                        if (derivedFlows != null && derivedFlows.Any())
                        {
                            foreach (var derivedFlow in derivedFlows)
                            {
                                var derivedItems = _derivedCollectionFlowService.GenerateItemsFromSourceField(
                                    derivedFlow.SourceFieldId, FormData, derivedFlow);
                                
                                if (!derivedItems.Any())
                                {
                                    // Use template-defined error message or fallback to default
                                    var errorMessage = !string.IsNullOrEmpty(derivedFlow.NoItemsErrorMessage)
                                        ? derivedFlow.NoItemsErrorMessage
                                        : $"You need to add at least one item before signing the {derivedFlow.Title}";
                                    errorLines.Add(errorMessage);
                                    continue;
                                }
                                
                                var statuses = _derivedCollectionFlowService.GetItemStatuses(derivedFlow.FieldId, FormData);
                                
                                var unsignedItems = derivedItems
                                    .Where(item => !statuses.ContainsKey(item.Id) || statuses[item.Id] != "Signed")
                                    .ToList();
                                
                                if (unsignedItems.Any())
                                {
                                    foreach (var item in unsignedItems)
                                    {
                                        var displayName = GetDerivedItemDisplayName(derivedFlow, item.Id);
                                        // Use template-defined error message or fallback to default
                                        var errorMessage = !string.IsNullOrEmpty(derivedFlow.UnsignedItemErrorMessage)
                                            ? derivedFlow.UnsignedItemErrorMessage.Replace("{sourceName}", displayName)
                                            : $"You need to sign the declaration for {displayName}";
                                        errorLines.Add(errorMessage);
                                    }
                                }
                            }
                        }
                        
                        if (errorLines.Any())
                        {
                            ModelState.Clear();
                            // Add header message
                            ModelState.AddModelError("", "You cannot mark this section as complete:");
                            // Add each error as a separate ModelState entry so they render as bullet points
                            foreach (var errorLine in errorLines)
                            {
                                ModelState.AddModelError("", errorLine);
                            }
                            IsTaskCompleted = false;
                            
                            // CRITICAL: Ensure CurrentFormState is set correctly for the view to render properly
                            CurrentFormState = FormState.DerivedCollectionFlowSummary;
                            
                            // CRITICAL: Load FormData from session so the view can render the derived flow sections
                            LoadFormDataFromSession();
                            
                            return Page();
                        }
                    }

                    if (ApplicationId.HasValue)
                    {
                        try
                        {
                            if (isCompleted)
                            {
                                await _applicationStateService.SaveTaskStatusAsync(
                                    ApplicationId.Value,
                                    CurrentTask.TaskId,
                                    Domain.Models.TaskStatus.Completed,
                                    HttpContext.Session);
                                
                            }
                            else
                            {
                                var hasAnyData = _applicationStateService.CalculateTaskStatus(CurrentTask.TaskId, Template, FormData, ApplicationId, HttpContext.Session, ApplicationStatus)
                                    != Domain.Models.TaskStatus.NotStarted;
                                var newStatus = hasAnyData ? Domain.Models.TaskStatus.InProgress : Domain.Models.TaskStatus.NotStarted;
                                await _applicationStateService.SaveTaskStatusAsync(
                                    ApplicationId.Value,
                                    CurrentTask.TaskId,
                                    newStatus,
                                    HttpContext.Session);
                                
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "POST (fallback): Failed to save status for task '{TaskId}'", CurrentTask.TaskId);
                        }
                    }

                    var taskListUrl = _formNavigationService.GetTaskListUrl(ReferenceNumber);
                    
                    return Redirect(taskListUrl);
                }
                var summaryUrl = $"/applications/{ReferenceNumber}/{CurrentTask.TaskId}";
                return Redirect(summaryUrl);
            }
            // Fallback: redirect to task list if CurrentTask is null
            var listUrl = $"/applications/{ReferenceNumber}";
            return Redirect(listUrl);
        }

        public async Task<IActionResult> OnGetAutocompleteAsync(string endpoint, string query)
        {
            

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                
                return new JsonResult(new List<object>());
            }

            try
            {
                var results = await autocompleteService.SearchAsync(endpoint, query);
                _logger.LogInformation("Autocomplete search returned {Count} results", results.Count);
                return new JsonResult(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in autocomplete search endpoint: {Endpoint}, query: {Query}", endpoint, query);
                return new JsonResult(new List<object>());
            }
        }

        // Removed: superseded by RemoveFieldItem page handler

        public async Task<IActionResult> OnPostRemoveCollectionItemAsync(string fieldId, string itemId, string? flowId = null)
        {
            await CommonFormEngineInitializationAsync();
            
            ModelState.Clear();
            
            if (!string.IsNullOrEmpty(TaskId))
            {
                var (group, task) = InitializeCurrentTask(TaskId);
                CurrentGroup = group;
                CurrentTask = task;
            }
            
            if (string.IsNullOrEmpty(fieldId) || string.IsNullOrEmpty(itemId))
            {
                return BadRequest("Field ID and Item ID are required");
            }

            bool isConfirmed = Request.Query.ContainsKey("confirmed") && Request.Query["confirmed"] == "true";
            
            if (!isConfirmed)
            {
                _logger.LogInformation("RemoveCollectionItem handler executing for validation - item will not be removed yet");
                
                return Redirect(_formNavigationService.GetCollectionFlowSummaryUrl(TaskId, ReferenceNumber));
            }
            
            _logger.LogInformation("RemoveCollectionItem handler executing confirmed removal for item {ItemId} from field {FieldId}", itemId, fieldId);

            // Get current collection from session first
            var accumulatedData = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
            
            Dictionary<string, object>? itemData = null;
            string? flowTitle = null;
            
            // Get the flow and item information for success message
            if (!string.IsNullOrEmpty(flowId) && CurrentTask != null)
            {
                var flow = CurrentTask.Summary?.Flows?.FirstOrDefault(f => f.FlowId == flowId);
                if (flow != null)
                {
                    flowTitle = flow.Title;
                    
                    // Get the item data before removing it
                    if (accumulatedData.TryGetValue(fieldId, out var collectionValue))
                    {
                        var json = collectionValue?.ToString() ?? "[]";
                        try
                        {
                            var items = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json) ?? new();
                            itemData = items.FirstOrDefault(i => i.TryGetValue("id", out var id) && id?.ToString() == itemId);
                        }
                        catch { }
                    }
                    
                    // Generate success message using custom message or fallback
                    SuccessMessage = GenerateSuccessMessage(flow.DeleteItemMessage, "delete", itemData, flowTitle);
                }
            }

            // Now perform the actual removal
            if (accumulatedData.TryGetValue(fieldId, out var collectionData))
            {
                var json = collectionData?.ToString() ?? "[]";
                try
                {
                    var items = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json) ?? new();
                    
                    // Remove the item with matching ID
                    items.RemoveAll(item => item.TryGetValue("id", out var id) && id?.ToString() == itemId);
                    
                    // Update the collection
                    var updatedJson = JsonSerializer.Serialize(items);
                    _applicationResponseService.AccumulateFormData(new Dictionary<string, object> { [fieldId] = updatedJson }, HttpContext.Session);
                    
                    // Save to API
                    if (ApplicationId.HasValue)
                    {
                        await _applicationResponseService.SaveApplicationResponseAsync(ApplicationId.Value, new Dictionary<string, object> { [fieldId] = updatedJson }, HttpContext.Session);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to remove collection item {ItemId} from field {FieldId}", itemId, fieldId);
                }
            }

            // Redirect back to the collection summary
            return Redirect(_formNavigationService.GetCollectionFlowSummaryUrl(TaskId, ReferenceNumber));
        }

        public async Task<IActionResult> OnGetComplexFieldAsync(string complexFieldId, string query)
        {
            _logger.LogInformation("Complex field search called with complexFieldId: {ComplexFieldId}, query: {Query}", complexFieldId, query);

            if (string.IsNullOrWhiteSpace(complexFieldId))
            {
                _logger.LogWarning("Complex field search called without complexFieldId");
                return new JsonResult(new List<object>());
            }

            try
            {
                
                var results = await autocompleteService.SearchAsync(complexFieldId, query);
                
                return new JsonResult(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in complex field search complexFieldId: {ComplexFieldId}, query: {Query}", complexFieldId, query);
                return new JsonResult(new List<object>());
            }
        }



        private static bool TryParseFlowRoute(string pageId, out string flowId, out string instanceId, out string flowPageId)
        {
            flowId = instanceId = flowPageId = string.Empty;
            if (string.IsNullOrEmpty(pageId)) return false;
            // Expected: flow/{flowId}/{instanceId}/{pageId?}
            var parts = pageId.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[0].Equals("flow", StringComparison.OrdinalIgnoreCase))
            {
                flowId = parts[1];
                instanceId = parts[2];
                flowPageId = parts.Length > 3 ? parts[3] : string.Empty;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Parses derived collection flow routes like: {flowId}/derived/{itemId}/{pageId?}
        /// </summary>
        private static bool TryParseDerivedFlowRoute(string pageId, out string derivedFlowId, out string derivedItemId, out string derivedPageId)
        {
            derivedFlowId = derivedItemId = derivedPageId = string.Empty;
            if (string.IsNullOrEmpty(pageId)) return false;
            
            // Expected: {flowId}/derived/{itemId}/{pageId?}
            var parts = pageId.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[1].Equals("derived", StringComparison.OrdinalIgnoreCase))
            {
                derivedFlowId = parts[0];
                derivedItemId = parts[2];
                derivedPageId = parts.Length > 3 ? parts[3] : string.Empty;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the pages for a specific flow in multi-collection flow mode
        /// </summary>
        private List<Domain.Models.Page>? GetFlowPages(Domain.Models.Task? task, string flowId)
        {
            var flow = task?.Summary?.Flows?.FirstOrDefault(f => f.FlowId == flowId);
            return flow?.Pages;
        }

        /// <summary>
        /// Gets the fieldId for a specific flow in multi-collection flow mode
        /// </summary>
        private string? GetFlowFieldId(Domain.Models.Task? task, string flowId)
        {
            var flow = task?.Summary?.Flows?.FirstOrDefault(f => f.FlowId == flowId);
            return flow?.FieldId;
        }

        /// <summary>
        /// Gets the configuration for a specific derived flow
        /// </summary>
        private DerivedCollectionFlowConfiguration? GetDerivedFlowConfiguration(Domain.Models.Task? task, string derivedFlowId)
        {
            _logger.LogInformation("GetDerivedFlowConfiguration: Looking for flowId='{FlowId}' in task '{TaskId}'", derivedFlowId, task?.TaskId);
            _logger.LogInformation("GetDerivedFlowConfiguration: Task summary mode: '{Mode}'", task?.Summary?.Mode);
            _logger.LogInformation("GetDerivedFlowConfiguration: DerivedFlows count: {Count}", task?.Summary?.DerivedFlows?.Count ?? 0);
            
            if (task?.Summary?.DerivedFlows != null)
            {
                foreach (var flow in task.Summary.DerivedFlows)
                {
                    _logger.LogInformation("GetDerivedFlowConfiguration: Available flow - FlowId='{FlowId}', FieldId='{FieldId}'", flow.FlowId, flow.FieldId);
                }
            }
            
            var derivedFlow = task?.Summary?.DerivedFlows?.FirstOrDefault(f => f.FlowId == derivedFlowId);
            _logger.LogInformation("GetDerivedFlowConfiguration: Found config: {Found}", derivedFlow != null);
            return derivedFlow;
        }

        /// <summary>
        /// Loads pre-filled data for a derived collection item
        /// </summary>
        private void LoadDerivedItemData(DerivedCollectionFlowConfiguration config, string itemId)
        {
            try
            {
                // First, load any existing declaration data for this item
                var existingData = _derivedCollectionFlowService.GetItemDeclarationData(config.FieldId, itemId, FormData);
                foreach (var kvp in existingData)
                {
                    Data[kvp.Key] = kvp.Value;
                }

                // Then, generate and load pre-filled data from the source
                var derivedItems = _derivedCollectionFlowService.GenerateItemsFromSourceField(config.SourceFieldId, FormData, config);
                var currentItem = derivedItems.FirstOrDefault(item => item.Id == itemId);
                
                if (currentItem != null)
                {
                    // Pre-fill with source data (but don't overwrite existing declaration data)
                    foreach (var kvp in currentItem.PrefilledData)
                    {
                        if (!Data.ContainsKey(kvp.Key)) // Only set if not already populated from existing data
                        {
                            Data[kvp.Key] = kvp.Value;
                        }
                    }
                    
                    _logger.LogInformation("Loaded derived item data for item {ItemId} in flow {FlowId} with {Count} fields", 
                        itemId, config.FlowId, currentItem.PrefilledData.Count);
                }
                
                // Ensure all field labels are visible for derived flow forms
                if (CurrentPage != null)
                {
                    foreach (var field in CurrentPage.Fields)
                    {
                        if (field.Label != null)
                        {
                            field.Label.IsVisible = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load derived item data for item {ItemId} in flow {FlowId}", itemId, config.FlowId);
            }
        }

        /// <summary>
        /// Resolves a user-friendly display name for a derived item, using the service's generated
        /// items and the configured binding. Falls back to the raw itemId if no data is available.
        /// </summary>
        private string GetDerivedItemDisplayName(DerivedCollectionFlowConfiguration config, string itemId)
        {
            try
            {
                var items = _derivedCollectionFlowService.GenerateItemsFromSourceField(config.SourceFieldId, FormData, config);
                var match = items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    if (!string.IsNullOrWhiteSpace(match.DisplayName))
                    {
                        return match.DisplayName;
                    }

                    if (match.PrefilledData != null &&
                        match.PrefilledData.TryGetValue(config.ItemTitleBinding, out var value) &&
                        !string.IsNullOrWhiteSpace(value?.ToString()))
                    {
                        return value!.ToString()!;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return itemId;
        }

        /// <summary>
        /// Checks if an item with the given instanceId already exists in the collection
        /// </summary>
        private bool IsExistingCollectionItem(string fieldId, string instanceId)
        {
            var accumulated = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
            if (accumulated.TryGetValue(fieldId, out var collectionValue))
            {
                var json = collectionValue?.ToString() ?? "[]";
                try
                {
                    var items = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json) ?? new();
                    return items.Any(item => item.TryGetValue("id", out var id) && id?.ToString() == instanceId);
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        /// <summary>
        /// Reads a collection field value from FormData and parses it to a list of item dictionaries.
        /// Returns an empty list when missing or invalid.
        /// </summary>
        private List<Dictionary<string, object>> ReadCollectionItemsFromFormData(string fieldId)
        {
            if (!FormData.TryGetValue(fieldId, out var value) || value == null)
            {
                return new List<Dictionary<string, object>>();
            }
            var s = value.ToString();
            if (string.IsNullOrWhiteSpace(s) || !s!.TrimStart().StartsWith("["))
            {
                return new List<Dictionary<string, object>>();
            }
            try
            {
                var parsed = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(s);
                return parsed ?? new List<Dictionary<string, object>>();
            }
            catch
            {
                return new List<Dictionary<string, object>>();
            }
        }

        /// <summary>
        /// Checks if a task has any data (for regular tasks or collection flows)
        /// </summary>
        private bool HasAnyTaskData(Domain.Models.Task task)
        {
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
                
            return taskFieldIds.Any(fieldId => 
                FormData.ContainsKey(fieldId) && 
                !string.IsNullOrWhiteSpace(FormData[fieldId]?.ToString()));
        }

        private void AppendCollectionItemToSession(List<Domain.Models.Page> pages, string fieldId, string instanceId, Dictionary<string, object> itemData)
        {
            var acc = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
            var list = new List<Dictionary<string, object>>();
            if (acc.TryGetValue(fieldId, out var existing))
            {
                var s = existing?.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(s);
                        if (parsed != null) list = parsed;
                    }
                    catch { }
                }
            }



            // Find existing item or create new one
            var idx = list.FindIndex(x => x.TryGetValue("id", out var id) && id?.ToString() == instanceId);
            Dictionary<string, object> item;
            
            if (idx >= 0)
            {
                // Editing existing item: start with existing data and merge in new values
                item = new Dictionary<string, object>(list[idx]);
                
                // Update only the fields that have values in itemData (current page data)
                foreach (var kvp in itemData)
                {
                    // If the incoming value is the upload placeholder, do not overwrite an existing upload JSON
                    if (kvp.Value?.ToString() == "UPLOAD_FIELD_SESSION_DATA" &&
                        item.TryGetValue(kvp.Key, out var existingVal) &&
                        existingVal != null && existingVal.ToString()!.StartsWith("[") && existingVal.ToString()!.Contains("\"id\""))
                    {
                        continue;
                    }
                    item[kvp.Key] = kvp.Value;
                }
            }
            else
            {
                // New item: create fresh item with all possible fields from flow pages
                item = new Dictionary<string, object>();
                foreach (var page in pages)
                {
                    foreach (var field in page.Fields)
                    {
                        var key = field.FieldId;
                        if (itemData.TryGetValue(key, out var value))
                        {
                            // Skip placeholder writes for uploads; real value will be in itemData when available
                            if (value?.ToString() == "UPLOAD_FIELD_SESSION_DATA")
                            {
                                continue;
                            }
                            item[key] = value;
                        }
                    }
                }
                item["id"] = instanceId;
            }

            // Ensure id is always set
            item["id"] = instanceId;

            // DEBUG: Log final item before serialization

            foreach (var kvp in item)
            {
                var valueStr = kvp.Value?.ToString();
                var preview = valueStr?.Length > 100 ? valueStr.Substring(0, 100) + "..." : valueStr;

                if (kvp.Key.Contains("upload", StringComparison.OrdinalIgnoreCase))
                {

                }
            }

            // Upsert the item
            if (idx >= 0) 
                list[idx] = item; 
            else 
                list.Add(item);

            var serialized = JsonSerializer.Serialize(list);

            _applicationResponseService.AccumulateFormData(new Dictionary<string, object> { [fieldId] = serialized }, HttpContext.Session);
        }

        private static string GetFlowProgressSessionKey(string flowId, string instanceId) => $"FlowProgress_{flowId}_{instanceId}";

        private Dictionary<string, object> LoadFlowProgressWithDebug()
        {
            if (!IsCollectionFlow)
            {

                return new Dictionary<string, object>();
            }

            var key = GetFlowProgressSessionKey(FlowId, InstanceId);

            


            
            // Try to get all session keys
            try
            {
                var sessionKeys = new List<string>();
                foreach (var sessionKey in HttpContext.Session.Keys)
                {
                    sessionKeys.Add(sessionKey);
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UPLOAD DEBUG] Error getting session keys: {ex.Message}");
            }
            
            var json = HttpContext.Session.GetString(key);
            if (string.IsNullOrWhiteSpace(json)) 
            {

                return new Dictionary<string, object>();
            }
            
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();

                return data;
            }
            catch (Exception ex)
            {

                return new Dictionary<string, object>();
            }
        }

        private Dictionary<string, object> LoadFlowProgress(string flowId, string instanceId)
        {
            var key = GetFlowProgressSessionKey(flowId, instanceId);
            var json = HttpContext.Session.GetString(key);
            if (string.IsNullOrWhiteSpace(json)) 
            {


                return new Dictionary<string, object>();
            }
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

                return dict ?? new Dictionary<string, object>();
            }
            catch
            {

                return new Dictionary<string, object>();
            }
        }

        private void SaveFlowProgress(string flowId, string instanceId, Dictionary<string, object> latest)
        {
            var existing = LoadFlowProgress(flowId, instanceId);
            foreach (var kv in latest)
            {
                existing[kv.Key] = kv.Value;
            }
            var key = GetFlowProgressSessionKey(flowId, instanceId);
            HttpContext.Session.SetString(key, JsonSerializer.Serialize(existing));
            

        }

        private void ClearFlowProgress(string flowId, string instanceId)
        {
            var key = GetFlowProgressSessionKey(flowId, instanceId);
            HttpContext.Session.Remove(key);
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

        private async Task LoadAccumulatedDataFromSessionAsync()
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

            // Apply conditional logic after loading data
            await ApplyConditionalLogicAsync();
        }

                private async Task ApplyConditionalLogicAsync(string trigger = "load")
                    {
                        try
                        {
                

                if (Template?.ConditionalLogic != null && Template.ConditionalLogic.Any())
                {
                    // Log all rules in template
                    foreach (var rule in Template.ConditionalLogic)
                    {
                        
                        foreach (var condition in rule.ConditionGroup.Conditions)
                        {
                            
                        }
                    }

                    var dataForConditionalLogic = new Dictionary<string, object>(Data);
                    
                    // Only merge when in POST/change trigger (not during initial GET/load)
                    if (trigger == "change")
                    {
                        var accumulatedData = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
                        foreach (var kvp in accumulatedData)
                        {
                            // Only add if not already in dataForConditionalLogic (current page data takes priority)
                            if (!dataForConditionalLogic.ContainsKey(kvp.Key))
                            {
                                dataForConditionalLogic[kvp.Key] = kvp.Value;
                            }
                        }
                    }

                    var context = new ConditionalLogicContext
                    {
                        CurrentPageId = CurrentPageId,
                        CurrentTaskId = TaskId,
                        IsClientSide = false,
                        Trigger = trigger
                    };

                    ConditionalState = await _conditionalLogicOrchestrator.ApplyConditionalLogicAsync(Template, dataForConditionalLogic, context);
                    
                    
                    
                    // Apply field values from conditional logic
                    if (ConditionalState.FieldValues.Any())
                    {
                        foreach (var kvp in ConditionalState.FieldValues)
                        {
                            Data[kvp.Key] = kvp.Value;
                        }
                        
                    }
                }
                else
                {
                    
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CONDITIONAL LOGIC ERROR: {Message}", ex.Message);
            }
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
                var status = GetTaskStatusFromSession(task.TaskId);
                return status == Domain.Models.TaskStatus.InProgress || status == Domain.Models.TaskStatus.Completed;
            });

            return hasAnyTaskWithProgress ? "InProgress" : "InProgress"; // Always InProgress until submitted
        }

        private void LoadExistingFlowItemData(string flowId, string instanceId)
        {
            // Check if we're editing an existing item by looking in the collection
            var task = CurrentTask;
            var fieldId = GetFlowFieldId(task, flowId);
            
            if (string.IsNullOrEmpty(fieldId)) return;

            var accumulated = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
            if (accumulated.TryGetValue(fieldId, out var collectionValue))
            {
                var json = collectionValue?.ToString() ?? "[]";
                try
                {
                    var items = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json) ?? new();
                    var existingItem = items.FirstOrDefault(item => item.TryGetValue("id", out var id) && id?.ToString() == instanceId);
                    
                    if (existingItem != null)
                    {
                        // Editing existing item: load its data into Data dictionary for form rendering
                        foreach (var kvp in existingItem)
                        {
                            if (kvp.Key == "id") continue; // Skip the ID field
                            // Preserve upload data if present in saved item
                            if (kvp.Value != null && kvp.Value.ToString()?.StartsWith("[") == true && kvp.Value.ToString()!.Contains("\"id\""))
                            {
                                Data[kvp.Key] = kvp.Value;
                                continue;
                            }
                            Data[kvp.Key] = kvp.Value;
                        }

                    }
                    else
                    {
                        // New item: check if this is the first page or if we have progress
                        var existingProgress = LoadFlowProgress(flowId, instanceId);
                        if (existingProgress.Any())
                        {
                            // We have progress, this is not the first page - load the progress
                            foreach (var kvp in existingProgress)
                            {
                                Data[kvp.Key] = kvp.Value;
                            }

                        }
                        else
                        {
                            // No progress exists, this is likely the first page - ensure clean start
                            ClearFlowProgress(flowId, instanceId);
                            Data.Clear();

                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load existing flow item data for instance {InstanceId}", instanceId);
                }
            }
            else
            {
                // No collection exists yet - check for existing progress
                var existingProgress = LoadFlowProgress(flowId, instanceId);
                if (existingProgress.Any())
                {
                    // Load existing progress
                    foreach (var kvp in existingProgress)
                    {
                        Data[kvp.Key] = kvp.Value;
                    }

                }
                else
                {
                    // Truly new - clear everything
                    ClearFlowProgress(flowId, instanceId);
                    Data.Clear();

                }
            }
        }

        /// <summary>
        /// Generates a success message using custom template or fallback default
        /// </summary>
        /// <param name="customMessage">Custom message template from configuration</param>
        /// <param name="operation">Operation type: "add", "update", or "delete"</param>
        /// <param name="itemData">Dictionary containing all field values for the item</param>
        /// <param name="flowTitle">Title of the flow</param>
        /// <returns>Formatted success message</returns>
        private string GenerateSuccessMessage(string? customMessage, string operation, Dictionary<string, object>? itemData, string? flowTitle)
        {
            // If custom message is provided, use it with placeholder substitution
            if (!string.IsNullOrEmpty(customMessage))
            {
                var message = customMessage;
                
                // Replace {flowTitle} placeholder
                message = message.Replace("{flowTitle}", flowTitle ?? "collection");
                
                // Replace field-based placeholders like {firstName}, {gender}, etc.
                if (itemData != null)
                {
                    foreach (var kvp in itemData)
                    {
                        var placeholder = $"{{{kvp.Key}}}";
                        var value = kvp.Value?.ToString() ?? "";
                        message = message.Replace(placeholder, value);
                    }
                }
                
                return message;
            }

            // Fallback to default messages - try to use itemTitleBinding or first available field
            string displayName = "Item";
            if (itemData != null && itemData.Any())
            {
                // Try common name fields first, then fall back to any non-empty value
                var nameFields = new[] { "firstName", "name", "title", "label" };
                var nameField = nameFields.FirstOrDefault(field => itemData.ContainsKey(field) && !string.IsNullOrEmpty(itemData[field]?.ToString()));
                
                if (nameField != null)
                {
                    displayName = itemData[nameField]?.ToString() ?? "Item";
                }
                else
                {
                    // Use the first non-empty field value
                    var firstValue = itemData.Values.FirstOrDefault(v => !string.IsNullOrEmpty(v?.ToString()));
                    if (firstValue != null)
                    {
                        displayName = firstValue.ToString() ?? "Item";
                    }
                }
            }

            var lowerFlowTitle = flowTitle?.ToLowerInvariant() ?? "collection";

            return operation switch
            {
                "add" => $"{displayName} has been added to {lowerFlowTitle}",
                "update" => $"{displayName} has been updated",
                "delete" => $"{displayName} has been removed from {lowerFlowTitle}",
                _ => $"{displayName} has been processed"
            };
        }

        /// <summary>
        /// Check if a field should be hidden based on conditional logic
        /// </summary>
        /// <param name="fieldId">The field ID to check</param>
        /// <returns>True if the field should be hidden</returns>
        public bool IsFieldHidden(string fieldId)
        {
            if (ConditionalState == null)
            {
                // If no conditional state but field has conditional logic rules, hide it by default
                if (Template?.ConditionalLogic != null && HasFieldConditionalLogic(fieldId))
                {
                    return true;
                }
                return false;
            }

            if (ConditionalState.FieldVisibility.TryGetValue(fieldId, out var isVisible))
            {
                return !isVisible;
            }
            
            // Check if field has conditional logic rules - if so, hide by default until conditions are met
            if (Template?.ConditionalLogic != null && HasFieldConditionalLogic(fieldId))
            {
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Check if a field has conditional logic rules that affect its visibility
        /// </summary>
        /// <param name="fieldId">The field ID to check</param>
        /// <returns>True if the field has conditional visibility rules</returns>
        private bool HasFieldConditionalLogic(string fieldId)
        {
            if (Template?.ConditionalLogic == null) return false;
            
            return Template.ConditionalLogic.Any(rule => 
                rule.Enabled && 
                rule.AffectedElements.Any(element => 
                    element.ElementId == fieldId && 
                    element.ElementType == "field" && 
                    (element.Action == "hide" || element.Action == "show")));
        }

        /// <summary>
        /// Check if a page should be hidden/skipped based on conditional logic
        /// </summary>
        /// <param name="pageId">The page ID to check</param>
        /// <returns>True if the page should be hidden</returns>
        public bool IsPageHidden(string pageId)
        {

            
            if (ConditionalState == null)
            {

                // If no conditional state but page has conditional logic rules, hide it by default
                if (Template?.ConditionalLogic != null && HasPageConditionalLogic(pageId))
                {

                    return true;
                }
                return false;
            }

            // Check if page is in skipped list
            if (ConditionalState.SkippedPages.Contains(pageId))
            {

                return true;
            }

            // Check if page is hidden by visibility rules
            if (ConditionalState.PageVisibility.TryGetValue(pageId, out var isVisible))
            {
                // Trust the ConditionalState that was already calculated by ApplyConditionalLogicAsync
                return !isVisible;
            }
            
            // If page is not in ConditionalState.PageVisibility but has conditional logic rules, hide it by default
            if (Template?.ConditionalLogic != null && HasPageConditionalLogic(pageId))
            {
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Check if a page has conditional logic rules that affect its visibility
        /// </summary>
        /// <param name="pageId">The page ID to check</param>
        /// <returns>True if the page has conditional visibility rules</returns>
        private bool HasPageConditionalLogic(string pageId)
        {
            if (Template?.ConditionalLogic == null) return false;
            
            return Template.ConditionalLogic.Any(rule => 
                rule.Enabled && 
                rule.AffectedElements.Any(element => 
                    element.ElementId == pageId && 
                    element.ElementType == "page" && 
                    (element.Action == "hide" || element.Action == "show" || element.Action == "skip")));
        }

        /// <summary>
        /// Check if conditional logic was actually triggered based on current data and field changes
        /// </summary>
        /// <returns>True if any conditional logic rules were triggered</returns>
        private bool HasConditionalLogicTriggered()
        {
            if (Template?.ConditionalLogic == null || ConditionalState == null)
            {
                return false;
            }

            // Check if any rules have their conditions met with current data
            foreach (var rule in Template.ConditionalLogic.Where(r => r.Enabled))
            {
                if (EvaluateRuleConditions(rule))
                {

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if conditional logic specifically shows/reveals new pages based on current form data
        /// </summary>
        /// <returns>True if conditional logic rules with "show" actions are met by current data</returns>
        private bool HasConditionalLogicShowingPages()
        {
            if (Template?.ConditionalLogic == null)
                return false;
            
            foreach (var rule in Template.ConditionalLogic.Where(r => r.Enabled))
            {
                // Only check rules that have "show" actions for pages
                var hasShowPageAction = rule.AffectedElements.Any(element => 
                    element.ElementType == "page" && element.Action == "show");
                
                if (!hasShowPageAction) continue;
                

                
                if (EvaluateRuleConditions(rule))
                {

                    return true;
                }
            }
            

            return false;
        }

        /// <summary>
        /// Evaluate if a conditional logic rule's conditions are met
        /// </summary>
        /// <param name="rule">The rule to evaluate</param>
        /// <returns>True if all conditions are met</returns>
        private bool EvaluateRuleConditions(Domain.Models.ConditionalLogic rule)
        {
            if (rule.ConditionGroup?.Conditions == null || !rule.ConditionGroup.Conditions.Any())
            {
                return false;
            }

            var results = new List<bool>();
            
            foreach (var condition in rule.ConditionGroup.Conditions)
            {
                var fieldValue = Data.TryGetValue(condition.TriggerField, out var value) ? value?.ToString() : "";
                var conditionValue = condition.Value?.ToString() ?? "";
                var conditionMet = condition.Operator.ToLower() switch
                {
                    "equals" => string.Equals(fieldValue, conditionValue, StringComparison.OrdinalIgnoreCase),
                    "not_equals" => !string.Equals(fieldValue, conditionValue, StringComparison.OrdinalIgnoreCase),
                    "contains" => fieldValue?.Contains(conditionValue, StringComparison.OrdinalIgnoreCase) == true,
                    "not_contains" => fieldValue?.Contains(conditionValue, StringComparison.OrdinalIgnoreCase) != true,
                    _ => false
                };
                
                results.Add(conditionMet);
            }

            // Apply logical operator
            return rule.ConditionGroup.LogicalOperator?.ToUpper() switch
            {
                "AND" => results.All(r => r),
                "OR" => results.Any(r => r),
                _ => results.All(r => r) // Default to AND
            };
        }

        /// <summary>
        /// Check if a field should be hidden for a specific collection item based on conditional logic
        /// </summary>
        /// <param name="fieldId">The field ID to check</param>
        /// <param name="itemData">The specific item's data to evaluate against</param>
        /// <returns>True if the field should be hidden for this specific item</returns>
        public bool IsFieldHiddenForItem(string fieldId, Dictionary<string, object> itemData)
        {
            try
            {
                if (Template?.ConditionalLogic == null || !Template.ConditionalLogic.Any())
                {
                    return false; // No conditional logic defined
                }

                var context = new ConditionalLogicContext
                {
                    CurrentPageId = CurrentPageId,
                    CurrentTaskId = TaskId,
                    IsClientSide = false,
                    Trigger = "load"
                };

                // Evaluate conditional logic synchronously using the specific item's data
                var itemConditionalState = _conditionalLogicOrchestrator.ApplyConditionalLogicAsync(Template, itemData, context).GetAwaiter().GetResult();
                
                if (itemConditionalState.FieldVisibility.TryGetValue(fieldId, out var isVisible))
                {
                    return !isVisible;
                }

                return false; // Default to visible if field not found in conditional logic
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating conditional logic for field {FieldId} with item data", fieldId);
                return false; // Default to visible on error
            }
        }

        #region Upload File Handlers

        public async Task<IActionResult> OnPostUploadFileAsync()
        {

            
            // Ensure Template is not null (required for RenderForm)
            if (Template == null)
            {
                Template = new FormTemplate
                {
                    TemplateId = "dummy",
                    TemplateName = "dummy",
                    Description = "dummy",
                    TaskGroups = new List<TaskGroup>()
                };
            }
            
            // Align POST context with GET so CurrentTask/Data are available
            try
            {
                await CommonFormEngineInitializationAsync();

            }
            catch (Exception ex)
            {

            }
            
            // Extract form data
            var applicationId = Request.Form["ApplicationId"].ToString();
            var fieldId = Request.Form["FieldId"].ToString();
            var returnUrl = Request.Form["ReturnUrl"].ToString();
            var uploadName = Request.Form["UploadName"].ToString();
            var uploadDescription = Request.Form["UploadDescription"].ToString();
            




            
            // Clear validation errors for FlowId/InstanceId if not in collection flow
            if (!IsCollectionFlow)
            {
                ModelState.Remove("FlowId");
                ModelState.Remove("InstanceId");

            }
            
            // Parse application ID
            if (!Guid.TryParse(applicationId, out var appId))
            {

                return NotFound();
            }
            
            // Get uploaded file
            var file = Request.Form.Files["UploadFile"];
            // Read any existing file IDs posted by the view to preserve list
            var existingFileIds = Request.Form["ExistingFileIds"].ToArray();

            
            if (file == null || file.Length == 0)
            {

                
                ErrorMessage = "Please select a file to upload.";
                ModelState.AddModelError("UploadFile", ErrorMessage);
                




                


                

                

                Files = await GetFilesForFieldAsync(appId, fieldId);

                
                // Check if we have return URL
                if (!string.IsNullOrEmpty(returnUrl))
                {

                    return Redirect(returnUrl);
                }
                




                return Page();
            }
            



            
            using var stream = file.OpenReadStream();
            var fileParam = new FileParameter(stream, file.FileName, file.ContentType);
            

            try
            {
                await fileUploadService.UploadFileAsync(appId, file.FileName, uploadDescription, fileParam);

                
                // SUCCESS PATH: Only execute this code if API call succeeds

                
                // Get and update files
                var currentFieldFiles = (await GetFilesForFieldAsync(appId, fieldId)).ToList();
                // Ensure we include files the UI already had (if backend lookup missed due to context)
                if (existingFileIds != null && existingFileIds.Length > 0)
                {
                    try
                    {
                        var allDbFilesForApp = await fileUploadService.GetFilesForApplicationAsync(appId);
                        var byId = new HashSet<string>(currentFieldFiles.Select(x => x.Id.ToString()));
                        foreach (var idStr in existingFileIds)
                        {
                            if (Guid.TryParse(idStr, out var guid) && !byId.Contains(guid.ToString()))
                            {
                                var match = allDbFilesForApp.FirstOrDefault(x => x.Id == guid);
                                if (match != null)
                                {
                                    currentFieldFiles.Add(match);
                                    byId.Add(guid.ToString());
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {

                    }
                }
                var allDbFiles = await fileUploadService.GetFilesForApplicationAsync(appId);
                var newlyUploadedFile = allDbFiles
                    .Where(f => !currentFieldFiles.Any(cf => cf.Id == f.Id))
                    .OrderByDescending(f => f.UploadedOn)
                    .FirstOrDefault();
                
                if (newlyUploadedFile != null)
                {
                    currentFieldFiles.Add(newlyUploadedFile);
                }
                
                UpdateSessionFileList(appId, fieldId, currentFieldFiles);
                await SaveUploadedFilesToResponseAsync(appId, fieldId, currentFieldFiles);
                
                // 1. Field-level key (used by the view partial)
                _formErrorStore.Clear(fieldId);
                // 2. Page-level context key (used by validation in OnPostPageAsync) - use same method to ensure exact match
                var pageContextKey = GetFormErrorContextKey();
                _formErrorStore.Clear(pageContextKey);
                // 3. Clear any errors already loaded into ModelState for this field
                ModelState.Remove(fieldId);
                ModelState.Remove($"Data[{fieldId}]");
                _logger.LogInformation("Cleared FormErrorStore (fieldKey: {FieldId}, contextKey: {PageContext}) and ModelState after successful upload", 
                    fieldId, pageContextKey);
                
                // Set success message
                SuccessMessage = $"Your file '{file.FileName}' uploaded.";

                
                // Send notification
                var addRequest = new AddNotificationRequest
                {
                    Message = SuccessMessage,
                    Category = "file-upload",
                    Context = fieldId + "FileUpload",
                    Type = NotificationType.Success,
                    AutoDismiss = false,
                    AutoDismissSeconds = 5
                };
                await _notificationsClient.CreateNotificationAsync(addRequest);

                
                // Redirect back if we have return URL
                if (!string.IsNullOrEmpty(returnUrl))
                {

                    return Redirect(returnUrl);
                }
                

                return Page();
            }
            catch (Exception ex)
            {



                // Don't handle the exception here - let the ExternalApiExceptionFilter handle it
                // This ensures that API errors get proper ModelState treatment
                throw;
            }
        }

        public async Task<IActionResult> OnPostDownloadFileAsync()
        {
            // Simple fix: Ensure Template is not null to prevent NullReferenceException
            if (Template == null)
            {
                Template = new FormTemplate 
                { 
                    TemplateId = "dummy", 
                    TemplateName = "dummy", 
                    Description = "dummy", 
                    TaskGroups = new List<TaskGroup>() 
                }; // Create empty template to prevent null reference

            }
            
            var applicationId = Request.Form["ApplicationId"].ToString();
            var fileIdStr = Request.Form["FileId"].ToString();
            
            if (!Guid.TryParse(applicationId, out var appId))
            {
                return NotFound();
            }
            if (!Guid.TryParse(fileIdStr, out var fileId))
            {
                return NotFound();
            }

            var fileResponse = await fileUploadService.DownloadFileAsync(fileId, appId);

            // Extract content type
            var contentType = fileResponse.Headers.TryGetValue("Content-Type", out var ct)
                ? ct.FirstOrDefault()
                : "application/octet-stream";

            string fileName = "downloadedfile";
            if (fileResponse.Headers.TryGetValue("Content-Disposition", out var cd))
            {
                var disposition = cd.FirstOrDefault();
                if (!string.IsNullOrEmpty(disposition))
                {
                    var fileNameMatch = System.Text.RegularExpressions.Regex.Match(
                        disposition,
                        @"filename\*=UTF-8''(?<fileName>.+)|filename=""?(?<fileName>[^\"";]+)""?"
                    );
                    if (fileNameMatch.Success)
                        fileName = System.Net.WebUtility.UrlDecode(fileNameMatch.Groups["fileName"].Value);
                }
            }

            return File(fileResponse.Stream, contentType, fileName);
        }

        public async Task<IActionResult> OnPostDeleteFileAsync()
        {
            // Clear any validation errors from previous POST requests
            // Without this, ModelState errors prevent confirmation from showing
            ModelState.Clear();
            
            // Simple fix: Ensure Template is not null to prevent NullReferenceException
            if (Template == null)
            {
                Template = new FormTemplate 
                { 
                    TemplateId = "dummy", 
                    TemplateName = "dummy", 
                    Description = "dummy", 
                    TaskGroups = new List<TaskGroup>() 
                }; // Create empty template to prevent null reference

            }
            
            var applicationId = Request.Form["ApplicationId"].ToString();
            var returnUrl = Request.Form["ReturnUrl"].ToString();
            var fileIdStr = Request.Form["FileId"].ToString();
            var fieldId = Request.Form["FieldId"].ToString();
            
            if (!Guid.TryParse(applicationId, out var appId))
                return NotFound();
                
            if (!Guid.TryParse(fileIdStr, out var fileId))
            {
                ModelState.AddModelError("FileId", "Invalid file ID.");
                
                // If we have a return URL, redirect back with error
                if (!string.IsNullOrEmpty(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                
                return Page();
            }

            bool isConfirmed = Request.Query.ContainsKey("confirmed") && Request.Query["confirmed"] == "true";
            
            if (!isConfirmed)
            {
                _logger.LogInformation("DeleteFile handler executing for validation - file will not be deleted yet");
                
                return Redirect(returnUrl);
            }
            
            _logger.LogInformation("DeleteFile handler executing confirmed deletion for file {FileId}", fileId);
            
            var addRequest = new AddNotificationRequest
            {
                Message = string.Empty, // set later when known
                Category = "file-upload",
                Context = fieldId + "FileDeletion",
                Type = NotificationType.Success
            };

            await fileUploadService.DeleteFileAsync(fileId, appId);

            
            // CRITICAL: Set success message for delete operation
            SuccessMessage = "File deleted.";


            // Get current files for this field and remove the deleted one
            var currentFieldFiles = (await GetFilesForFieldAsync(appId, fieldId)).ToList();
            currentFieldFiles.RemoveAll(f => f.Id == fileId);
            

            
            UpdateSessionFileList(appId, fieldId, currentFieldFiles);
            await SaveUploadedFilesToResponseAsync(appId, fieldId, currentFieldFiles);
            
            // If we have a return URL (from partial form), redirect back
            if (!string.IsNullOrEmpty(returnUrl))
            {
                // CRITICAL: Send notification for successful delete
                addRequest.Message = SuccessMessage;
                await _notificationsClient.CreateNotificationAsync(addRequest);

                
                return Redirect(returnUrl);
            }

            return Page();
        }

        private async Task<IReadOnlyList<UploadDto>> GetFilesForFieldAsync(Guid appId, string fieldId)
        {
            if (string.IsNullOrEmpty(fieldId))
            {
                return new List<UploadDto>().AsReadOnly();
            }




            if (IsCollectionFlow)
            {


                // CRITICAL FIX: Scan accumulated collection items to find this instance's item,
                // then read the inner field value (matching 'fieldId') like GET does.
                try
                {
                    var accumulatedData = applicationResponseService.GetAccumulatedFormData(HttpContext.Session);


                    foreach (var kvp in accumulatedData)
                    {
                        var collectionJson = kvp.Value?.ToString();
                        if (string.IsNullOrWhiteSpace(collectionJson))
                        {
                            continue;
                        }

                        try
                        {
                            var items = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(collectionJson) ?? new();
                            var existingItem = items.FirstOrDefault(item => item.TryGetValue("id", out var idVal) && idVal?.ToString() == InstanceId);
                            if (existingItem != null)
                            {

                                if (existingItem.TryGetValue(fieldId, out var innerValue) && innerValue != null)
                                {
                                    // Handle JsonElement array
                                    if (innerValue is JsonElement innerElem)
                                    {
                                        if (innerElem.ValueKind == JsonValueKind.Array)
                                        {
                                            try
                                            {
                                                var files = JsonSerializer.Deserialize<List<UploadDto>>(innerElem.GetRawText()) ?? new List<UploadDto>();

                                                return files.AsReadOnly();
                                            }
                                            catch (JsonException ex)
                                            {

                                            }
                                        }
                                    }
                                    // Handle string JSON
                                    else if (innerValue is string innerJson && !string.IsNullOrWhiteSpace(innerJson))
                                    {
                                        try
                                        {
                                            var files = JsonSerializer.Deserialize<List<UploadDto>>(innerJson) ?? new List<UploadDto>();

                                            return files.AsReadOnly();
                                        }
                                        catch (JsonException ex)
                                        {

                                        }
                                    }
                                    // Handle direct list
                                    else if (innerValue is List<UploadDto> uploadList)
                                    {

                                        return uploadList.AsReadOnly();
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Ignore parse errors for non-collection fields

                        }
                    }
                }
                catch (Exception ex)
                {

                }

                // FALLBACK: Check session flow progress

                var progressData = LoadFlowProgress(FlowId, InstanceId);


                if (progressData.TryGetValue(fieldId, out var progressValue))
                {
                    var sessionFilesJson = progressValue?.ToString();


                    if (!string.IsNullOrWhiteSpace(sessionFilesJson))
                    {
                        try
                        {
                            var files = JsonSerializer.Deserialize<List<UploadDto>>(sessionFilesJson);

                            return (files ?? new List<UploadDto>()).AsReadOnly();
                        }
                        catch (JsonException ex)
                        {

                        }
                    }
                }
            }
            else
            {
                // For regular forms, get files from session
                var sessionKey = $"UploadedFiles_{appId}_{fieldId}";
                var sessionFilesJson = HttpContext.Session.GetString(sessionKey);


                if (!string.IsNullOrWhiteSpace(sessionFilesJson))
                {
                    try
                    {
                        var files = JsonSerializer.Deserialize<List<UploadDto>>(sessionFilesJson) ?? new List<UploadDto>();

                        return files.AsReadOnly();
                    }
                    catch (Exception ex)
                    {

                    }
                }
            }


            return new List<UploadDto>().AsReadOnly();
        }

        private void UpdateSessionFileList(Guid appId, string fieldId, IReadOnlyList<UploadDto> files)
        {


            foreach (var file in files)
            {

            }
            
            if (IsCollectionFlow)
            {
                // For collection flows, store in flow progress system
                var progressKey = GetFlowProgressSessionKey(FlowId, InstanceId);


                
                // Debug: List all session keys before calling LoadFlowProgress
                var sessionKeysBefore = HttpContext.Session.Keys.ToList();

                
                // CRITICAL FIX: Use same method as page load for consistency
                var existingProgress = LoadFlowProgress(FlowId, InstanceId);

                
                // CRITICAL FIX: The 'files' parameter contains ALL files (existing + new), so just save it directly
                // No need to merge because GetFilesForFieldAsync already combined existing and new files
                var serializedFiles = JsonSerializer.Serialize(files);

                existingProgress[fieldId] = serializedFiles;
                
                // Force session to commit immediately
                var progressJson = JsonSerializer.Serialize(existingProgress);
                HttpContext.Session.SetString(progressKey, progressJson);
                

                
                // Flow progress saved successfully

            }
            else
            {
                // For regular forms, use the original session key
                var key = $"UploadedFiles_{appId}_{fieldId}";

                HttpContext.Session.SetString(key, JsonSerializer.Serialize(files));
            }
        }

        private async Task SaveUploadedFilesToResponseAsync(Guid appId, string fieldId, IReadOnlyList<UploadDto> files)
        {
            if (string.IsNullOrEmpty(fieldId))
            {
                return;
            }

            if (IsCollectionFlow)
            {
                // For collection flows, files are saved via flow progress system
                // This happens in UpdateSessionFileList, no need to save to main application response here
                return;
            }

            var json = JsonSerializer.Serialize(files);
            var data = new Dictionary<string, object> { { fieldId, json } };

            await applicationResponseService.SaveApplicationResponseAsync(appId, data, HttpContext.Session);
        }

        #endregion

        #region Form Error Store Helper Methods

        /// <summary>
        /// Gets a unique context key for storing form errors in session
        /// </summary>
        /// <returns>Form error context key</returns>
        private string GetFormErrorContextKey()
        {
            return $"{ReferenceNumber}_{TaskId}_{CurrentPageId}";
        }

        /// <summary>
        /// Restores previously saved form errors from session and applies them to ModelState
        /// </summary>
        private void RestoreFormErrors()
        {
            try
            {
                var contextKey = GetFormErrorContextKey();
                var (fieldErrors, generalError) = _formErrorStore.Load(contextKey, clearAfterRead: true);
                
                if (fieldErrors.Any())
                {
                    foreach (var kvp in fieldErrors)
                    {
                        foreach (var error in kvp.Value)
                        {
                            ModelState.AddModelError(kvp.Key, error);
                        }
                    }
                    _logger.LogInformation("DEBUG: Restored {ErrorCount} field errors from FormErrorStore with key: {ContextKey}", 
                        fieldErrors.Sum(x => x.Value.Count), contextKey);
                }
                
                if (!string.IsNullOrEmpty(generalError))
                {
                    ModelState.AddModelError("", generalError);
                    _logger.LogInformation("DEBUG: Restored general error from FormErrorStore: {GeneralError}", generalError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore form errors from session");
            }
        }

        #endregion

        #region Helper Methods for Field Requirement

        /// <summary>
        /// Gets a field from a task by field ID
        /// </summary>
        /// <param name="task">The task to search</param>
        /// <param name="fieldId">The field ID to find</param>
        /// <returns>The field if found, otherwise null</returns>
        private Field? GetFieldFromTask(Domain.Models.Task task, string fieldId)
        {
            if (task?.Pages == null) return null;

            foreach (var page in task.Pages)
            {
                if (page?.Fields == null) continue;

                var field = page.Fields.FirstOrDefault(f => f.FieldId == fieldId);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }

        #endregion

    }
}







