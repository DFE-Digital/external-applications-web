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
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Request;
using DfE.CoreLibs.Contracts.ExternalApplications.Enums;
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
        ILogger<RenderFormModel> logger)
        : BaseFormEngineModel(renderer, applicationResponseService, fieldFormattingService, templateManagementService,
            applicationStateService, formStateManager, formNavigationService, formDataManager, formValidationOrchestrator, formConfigurationService, logger)
    {
        private readonly IApplicationsClient _applicationsClient = applicationsClient;
        private readonly IConditionalLogicOrchestrator _conditionalLogicOrchestrator = conditionalLogicOrchestrator;
        private readonly INotificationsClient _notificationsClient = notificationsClient;
        private readonly IFormErrorStore _formErrorStore = formErrorStore;

        [BindProperty] public Dictionary<string, object> Data { get; set; } = new();

        [BindProperty] public bool IsTaskCompleted { get; set; }
        
        // Collection flow properties from form submission
        [BindProperty] public new string? FlowId { get; set; }
        [BindProperty] public new string? InstanceId { get; set; }
        [BindProperty] public string? FlowPageId { get; set; }
        
        // Calculate IsCollectionFlow automatically based on FlowId and InstanceId presence
        private bool IsCollectionFlow => !string.IsNullOrEmpty(FlowId) && !string.IsNullOrEmpty(InstanceId);

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
            await CommonFormEngineInitializationAsync();

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
                                
                                // Handle upload fields in GET requests to ensure Change button works correctly
                                Console.WriteLine($"[RENDERFORM GET DEBUG] Checking for upload fields in flow progress data");
                                foreach (var key in Data.Keys.ToList())
                                {
                                    var value = Data[key]?.ToString();
                                    if (!string.IsNullOrEmpty(value) && value.Contains("\"id\"") && value.Contains("\"originalFileName\""))
                                    {
                                        Console.WriteLine($"[RENDERFORM GET DEBUG] Found upload field {key} with data: {value.Substring(0, Math.Min(100, value.Length))}...");
                                        // Upload field data is already in correct format from session
                                    }
                                }
                                
                                _logger.LogInformation(">>>>>>>>>>>>SUB-FLOW GET: loaded data for flow {FlowId}, instance {InstanceId}, page {PageId}. Data: {Data}", 
                                    flowId, instanceId, flowPageId, string.Join(", ", Data.Select(kv => $"{kv.Key}:{kv.Value}")));
                                _logger.LogInformation(">>>>>>>>>>>>SUB-FLOW GET: existingMember field value = '{ExistingMemberValue}'", 
                                    Data.TryGetValue("existingMember", out var existingMemberVal) ? existingMemberVal : "NOT_FOUND");
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
                }
            }

            // Check if we need to clear session data for a new application
            CheckAndClearSessionForNewApplication();

            // Load accumulated form data from session to pre-populate fields (only if not in a sub-flow)
            if (string.IsNullOrEmpty(CurrentPageId) || !CurrentPageId.Contains("flow/"))
            {
                await LoadAccumulatedDataFromSessionAsync();
                // Apply conditional logic for regular pages too
                _logger.LogInformation(">>>>>>>>>>>>TASK SUMMARY GET: About to apply conditional logic for task summary");
                _logger.LogInformation(">>>>>>>>>>>>TASK SUMMARY DATA: {Data}", 
                    string.Join(", ", Data.Select(kv => $"{kv.Key}={kv.Value}")));
                await ApplyConditionalLogicAsync();
            }
            else
            {
                // For sub-flows, still apply conditional logic using the current Data
                await ApplyConditionalLogicAsync();
            }

            // Initialize task completion status if we're showing a task summary
            if (CurrentFormState == FormState.TaskSummary && CurrentTask != null)
            {
                var taskStatus = GetTaskStatusFromSession(CurrentTask.TaskId);
                IsTaskCompleted = taskStatus == Domain.Models.TaskStatus.Completed;
            }
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

            // Handle task completion checkbox state
            if (CurrentTask != null && ApplicationId.HasValue)
            {
                if (IsTaskCompleted)
                {
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
            await CommonFormEngineInitializationAsync();

            // Prevent editing if application is not editable
            if (!IsApplicationEditable())
            {
                return RedirectToPage("/FormEngine/RenderForm", new { referenceNumber = ReferenceNumber });
            }

            _logger.LogInformation("POST Page: ref={ReferenceNumber} task={TaskId} pageId={PageId}", ReferenceNumber, TaskId, CurrentPageId);

            
            // URL decode the pageId to handle encoded forward slashes from form submissions
            if (!string.IsNullOrEmpty(CurrentPageId))
            {
                CurrentPageId = System.Web.HttpUtility.UrlDecode(CurrentPageId);

            }

            if (!string.IsNullOrEmpty(CurrentPageId))
            {
                if (TryParseFlowRoute(CurrentPageId, out var flowId, out var instanceId, out var flowPageId))
                {
                    _logger.LogInformation("Detected sub-flow: flowId={FlowId} instance={InstanceId} flowPageId={FlowPageId}", flowId, instanceId, flowPageId);

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
                var (group, task) = InitializeCurrentTask(TaskId);
                CurrentGroup = group;
                CurrentTask = task;
                CurrentPage = null; // No specific page for task summary
            }

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

            // Apply conditional logic after processing form data changes
            _logger.LogInformation(">>>>>>>>>>>>POST: About to apply conditional logic after form submission");
            _logger.LogInformation(">>>>>>>>>>>>POST DATA BEFORE CONDITIONAL LOGIC: {Data}", 
                string.Join(", ", Data.Select(kv => $"{kv.Key}={kv.Value}")));
            
            // Debug: Check the actual upload field value received in POST
            if (Data.ContainsKey("trust-upload-flow-field"))
            {
                var uploadValue = Data["trust-upload-flow-field"];
                var uploadValueStr = uploadValue?.ToString() ?? "";
                Console.WriteLine($"[RENDERFORM POST DEBUG] Raw upload field value received: '{uploadValue}'");
                Console.WriteLine($"[RENDERFORM POST DEBUG] Upload field value length: {uploadValueStr.Length}");
                Console.WriteLine($"[RENDERFORM POST DEBUG] Upload field value type: {uploadValue?.GetType()?.Name ?? "null"}");
            }
            
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
                            Console.WriteLine($"[RENDERFORM POST DEBUG] Replaced {key} with session data: {sessionValue?.ToString()?.Substring(0, Math.Min(100, sessionValue?.ToString()?.Length ?? 0))}...");
                        }
                    }
                }
            }
            await ApplyConditionalLogicAsync("change");

            if (CurrentPage != null)
            {
                ValidateCurrentPage(CurrentPage, Data);
            }
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("ModelState invalid on POST Page: {Errors}", string.Join("; ", ModelState.Where(e => e.Value?.Errors.Count > 0).Select(k => $"{k.Key}:{string.Join('|', k.Value!.Errors.Select(er => er.ErrorMessage))}")));
                // If we're inside a sub-flow, redirect back to the same URL to avoid state mis-binding
                if (TryParseFlowRoute(CurrentPageId, out _, out _, out _))
                {
                    var selfUrl = $"/applications/{ReferenceNumber}/{TaskId}/{CurrentPageId}";
                    return Redirect(selfUrl);
                }
                return Page();
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
                                
                                // Look for the next visible page after current page
                                for (int i = index + 1; i < flowPages.Count; i++)
                                {
                                    var candidatePage = flowPages[i];
                                    
                                    // Check if this page should be skipped due to conditional logic
                                    var isHidden = ConditionalState.PageVisibility.TryGetValue(candidatePage.PageId, out var isVisible) && !isVisible;
                                    var isSkipped = ConditionalState.SkippedPages.Contains(candidatePage.PageId);
                                    
                                    _logger.LogDebug("Sub-flow navigation: checking page {PageId}, isHidden: {IsHidden}, isSkipped: {IsSkipped}, visibility: {Visibility}", 
                                        candidatePage.PageId, isHidden, isSkipped, isVisible);
                                    
                                    if (!isHidden && !isSkipped)
                                    {
                                        nextPageId = candidatePage.PageId;
                                        _logger.LogDebug("Sub-flow navigation: selected next page {NextPageId}", nextPageId);
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
                        }
                        else
                        {
                            // Flow complete: append item to collection and go back to collection summary
                            if (!string.IsNullOrEmpty(flowFieldId))
                            {
                                // Determine if this is a new item or an update
                                bool isNewItem = !IsExistingCollectionItem(flowFieldId, instanceId);
                                
                                // Merge accumulated progress with final page data
                                var accumulated = LoadFlowProgress(flowId, instanceId);
                Console.WriteLine($"[FLOW COMPLETE DEBUG] Loaded flow progress for {flowId}/{instanceId}:");
                foreach (var kv in accumulated)
                {
                    var valueStr = kv.Value?.ToString();
                    var preview = valueStr?.Length > 100 ? valueStr.Substring(0, 100) + "..." : valueStr;
                    Console.WriteLine($"[FLOW COMPLETE DEBUG] Progress - {kv.Key}: {preview}");
                    if (kv.Key.Contains("upload", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[FLOW COMPLETE DEBUG UPLOAD] Progress upload value: {valueStr}");
                    }
                }
                
                                foreach (var kv in Data)
                                {
                                    accumulated[kv.Key] = kv.Value;
                                }
                
                Console.WriteLine($"[FLOW COMPLETE DEBUG] Final accumulated data before saving to collection:");
                foreach (var kv in accumulated)
                {
                    var valueStr = kv.Value?.ToString();
                    var preview = valueStr?.Length > 100 ? valueStr.Substring(0, 100) + "..." : valueStr;
                    Console.WriteLine($"[FLOW COMPLETE DEBUG] Final - {kv.Key}: {preview}");
                    if (kv.Key.Contains("upload", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[FLOW COMPLETE DEBUG UPLOAD] Final upload value: {valueStr}");
                    }
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
                            }
                            var backToSummary = _formNavigationService.GetCollectionFlowSummaryUrl(CurrentTask.TaskId, ReferenceNumber);
                            return Redirect(backToSummary);
                        }
                    }
                }
                else
                {
                    // First check if returnToSummaryPage is true and should be respected
                    if (CurrentPage.ReturnToSummaryPage)
                    {
                        _logger.LogInformation(">>>>>>>>>>>>REGULAR NAV: returnToSummaryPage=true, checking if conditional logic should override");

                        
                        // Check if conditional logic suggests a different next page (override returnToSummaryPage)
                        string? conditionalNextPageId = null;
                        bool hasConditionalTrigger = false;
                        
                        if (ConditionalState != null && Template != null)
                        {
                            // FIXED: Check if conditional rules specifically show/reveal new pages, not just any trigger
                            hasConditionalTrigger = HasConditionalLogicShowingPages();
                            _logger.LogInformation(">>>>>>>>>>>>REGULAR NAV: Conditional logic shows new pages: {ShowsNewPages}", hasConditionalTrigger);
                            
                            if (hasConditionalTrigger)
                            {
                                var context = new ConditionalLogicContext
                                {
                                    CurrentPageId = CurrentPageId,
                                    CurrentTaskId = TaskId,
                                    IsClientSide = false,
                                    Trigger = "change"
                                };
                                
                                conditionalNextPageId = await _conditionalLogicOrchestrator.GetNextPageAsync(Template, Data, CurrentPage.PageId, context);
                                _logger.LogInformation(">>>>>>>>>>>>REGULAR NAV: Conditional logic suggested: {NextPageId}", conditionalNextPageId ?? "NULL");
                            }
                        }
                        
                        // If conditional logic found a next page AND was triggered, navigate there (override returnToSummaryPage)
                        if (hasConditionalTrigger && !string.IsNullOrEmpty(conditionalNextPageId))
                        {
                            var nextUrl = $"/applications/{ReferenceNumber}/{CurrentTask.TaskId}/{conditionalNextPageId}";
                            _logger.LogInformation(">>>>>>>>>>>>REGULAR NAV: Conditional logic OVERRIDES returnToSummaryPage - going to: {NextUrl}", nextUrl);
                            return Redirect(nextUrl);
                        }
                        
                        // No conditional override - respect returnToSummaryPage
                        var summaryUrl = _formNavigationService.GetTaskSummaryUrl(CurrentTask.TaskId, ReferenceNumber);
                        _logger.LogInformation(">>>>>>>>>>>>REGULAR NAV: Respecting returnToSummaryPage=true - going to summary: {SummaryUrl}", summaryUrl);
                        return Redirect(summaryUrl);
                    }
                    
                    // returnToSummaryPage=false - proceed with normal next page logic
                    string? nextPageId = null;
                    
                    if (ConditionalState != null && Template != null)
                    {
                        _logger.LogInformation(">>>>>>>>>>>>REGULAR NAV: returnToSummaryPage=false, using conditional logic for next page");
                        
                        var context = new ConditionalLogicContext
                        {
                            CurrentPageId = CurrentPageId,
                            CurrentTaskId = TaskId,
                            IsClientSide = false,
                            Trigger = "change"
                        };
                        
                        nextPageId = await _conditionalLogicOrchestrator.GetNextPageAsync(Template, Data, CurrentPage.PageId, context);
                        _logger.LogInformation(">>>>>>>>>>>>REGULAR NAV: Conditional logic suggested next page: {NextPageId}", nextPageId ?? "NULL");
                    }
                    
                    // If conditional logic found a next page, navigate to it
                    if (!string.IsNullOrEmpty(nextPageId))
                    {
                        var nextUrl = $"/applications/{ReferenceNumber}/{CurrentTask.TaskId}/{nextPageId}";
                        _logger.LogInformation(">>>>>>>>>>>>REGULAR NAV: Going to conditional next page: {NextUrl}", nextUrl);
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
                        _logger.LogInformation(">>>>>>>>>>>>REGULAR NAV: Going to sequential next page: {NextUrl}", nextUrl);
                        return Redirect(nextUrl);
                    }
                    
                    // No next page found - go to task summary as fallback
                    var fallbackUrl = _formNavigationService.GetTaskSummaryUrl(CurrentTask.TaskId, ReferenceNumber);
                    _logger.LogInformation(">>>>>>>>>>>>REGULAR NAV: No next page available - fallback to summary: {FallbackUrl}", fallbackUrl);
                    return Redirect(fallbackUrl);
                }
            }
            else if (CurrentTask != null)
            {
                // Fallback: redirect to task summary or collection summary depending on config
                if (_formStateManager.ShouldShowCollectionFlowSummary(CurrentTask))
                {
                    var url = _formNavigationService.GetCollectionFlowSummaryUrl(CurrentTask.TaskId, ReferenceNumber);
                    return Redirect(url);
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
            _logger.LogInformation("Autocomplete search called with endpoint: {Endpoint}, query: {Query}", endpoint, query);

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                _logger.LogWarning("Autocomplete search called without endpoint");
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

        public async Task<IActionResult> OnPostRemoveCollectionItemAsync(string fieldId, string itemId, string? flowId = null)
        {
            await CommonFormEngineInitializationAsync();
            
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
                _logger.LogInformation("Calling autocompleteService.SearchAsync with complexFieldId: {ComplexFieldId}, query: {Query}", complexFieldId, query);
                var results = await autocompleteService.SearchAsync(complexFieldId, query);
                _logger.LogInformation("Complex field search returned {Count} results for {ComplexFieldId}", results.Count, complexFieldId);
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

            // DEBUG: Log item data before processing
            Console.WriteLine($"[COLLECTION SAVE DEBUG] Processing item for field: {fieldId}, instanceId: {instanceId}");
            foreach (var kvp in itemData)
            {
                var valueStr = kvp.Value?.ToString();
                var preview = valueStr?.Length > 100 ? valueStr.Substring(0, 100) + "..." : valueStr;
                Console.WriteLine($"[COLLECTION SAVE DEBUG] Item data - {kvp.Key}: {preview}");
                if (kvp.Key.Contains("upload", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[COLLECTION SAVE DEBUG UPLOAD] Full upload value: {valueStr}");
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
                            item[key] = value;
                        }
                    }
                }
                item["id"] = instanceId;
            }

            // Ensure id is always set
            item["id"] = instanceId;

            // DEBUG: Log final item before serialization
            Console.WriteLine($"[COLLECTION SAVE DEBUG] Final item before serialization:");
            foreach (var kvp in item)
            {
                var valueStr = kvp.Value?.ToString();
                var preview = valueStr?.Length > 100 ? valueStr.Substring(0, 100) + "..." : valueStr;
                Console.WriteLine($"[COLLECTION SAVE DEBUG] Final item - {kvp.Key}: {preview}");
                if (kvp.Key.Contains("upload", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[COLLECTION SAVE DEBUG UPLOAD] Final upload value: {valueStr}");
                }
            }

            // Upsert the item
            if (idx >= 0) 
                list[idx] = item; 
            else 
                list.Add(item);

            var serialized = JsonSerializer.Serialize(list);
            Console.WriteLine($"[COLLECTION SAVE DEBUG] Serialized collection: {serialized.Substring(0, Math.Min(200, serialized.Length))}...");
            _applicationResponseService.AccumulateFormData(new Dictionary<string, object> { [fieldId] = serialized }, HttpContext.Session);
        }

        private static string GetFlowProgressSessionKey(string flowId, string instanceId) => $"FlowProgress_{flowId}_{instanceId}";

        private Dictionary<string, object> LoadFlowProgressWithDebug()
        {
            if (!IsCollectionFlow)
            {
                Console.WriteLine($"[UPLOAD DEBUG] Not a collection flow, returning empty progress");
                return new Dictionary<string, object>();
            }

            var key = GetFlowProgressSessionKey(FlowId, InstanceId);
            Console.WriteLine($"[UPLOAD DEBUG] Loading flow progress with key: {key}");
            
            // Debug: List all session keys to see what's actually in the session
            Console.WriteLine($"[UPLOAD DEBUG] Session ID: {HttpContext.Session.Id}");
            Console.WriteLine($"[UPLOAD DEBUG] Session is available: {HttpContext.Session.IsAvailable}");
            
            // Try to get all session keys
            try
            {
                var sessionKeys = new List<string>();
                foreach (var sessionKey in HttpContext.Session.Keys)
                {
                    sessionKeys.Add(sessionKey);
                }
                Console.WriteLine($"[UPLOAD DEBUG] All session keys: {string.Join(", ", sessionKeys)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UPLOAD DEBUG] Error getting session keys: {ex.Message}");
            }
            
            var json = HttpContext.Session.GetString(key);
            if (string.IsNullOrWhiteSpace(json)) 
            {
                Console.WriteLine($"[UPLOAD DEBUG] No flow progress found for key: {key}");
                return new Dictionary<string, object>();
            }
            
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
                Console.WriteLine($"[UPLOAD DEBUG] Successfully loaded flow progress with {data.Count} keys: {string.Join(", ", data.Keys)}");
                return data;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UPLOAD DEBUG] Error deserializing flow progress: {ex.Message}");
                return new Dictionary<string, object>();
            }
        }

        private Dictionary<string, object> LoadFlowProgress(string flowId, string instanceId)
        {
            var key = GetFlowProgressSessionKey(flowId, instanceId);
            Console.WriteLine($"[UPLOAD DEBUG] LoadFlowProgress called - Key: {key}");
            Console.WriteLine($"[UPLOAD DEBUG] Session ID in LoadFlowProgress: {HttpContext.Session.Id}");
            
            var json = HttpContext.Session.GetString(key);
            Console.WriteLine($"[UPLOAD DEBUG] Raw JSON from session: {(json?.Length > 0 ? json.Substring(0, Math.Min(200, json.Length)) + "..." : "NULL/EMPTY")}");
            
            if (string.IsNullOrWhiteSpace(json)) 
            {
                _logger.LogInformation(">>>>>>>>>>>>FLOW PROGRESS LOAD: flowId={FlowId}, instanceId={InstanceId}, result=EMPTY", flowId, instanceId);
                Console.WriteLine($"[UPLOAD DEBUG] LoadFlowProgress returning EMPTY for key: {key}");
                return new Dictionary<string, object>();
            }
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                _logger.LogInformation(">>>>>>>>>>>>FLOW PROGRESS LOAD: flowId={FlowId}, instanceId={InstanceId}, data={Data}", 
                    flowId, instanceId, string.Join(", ", (dict ?? new()).Select(kv => $"{kv.Key}={kv.Value}")));
                return dict ?? new Dictionary<string, object>();
            }
            catch
            {
                _logger.LogInformation(">>>>>>>>>>>>FLOW PROGRESS LOAD: flowId={FlowId}, instanceId={InstanceId}, result=ERROR", flowId, instanceId);
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
            
            _logger.LogInformation(">>>>>>>>>>>>FLOW PROGRESS SAVED: flowId={FlowId}, instanceId={InstanceId}, data={Data}", 
                flowId, instanceId, string.Join(", ", existing.Select(kv => $"{kv.Key}={kv.Value}")));
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

                    var context = new ConditionalLogicContext
                    {
                        CurrentPageId = CurrentPageId,
                        CurrentTaskId = TaskId,
                        IsClientSide = false,
                        Trigger = trigger
                    };

                    ConditionalState = await _conditionalLogicOrchestrator.ApplyConditionalLogicAsync(Template, Data, context);
                    
                    
                    
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
                            if (kvp.Key != "id") // Skip the ID field
                            {
                                Data[kvp.Key] = kvp.Value;
                            }
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
            _logger.LogInformation(">>>>>>>>>>>>FIELD VISIBILITY CHECK: Checking field '{FieldId}'", fieldId);
            
            if (ConditionalState == null)
            {
                _logger.LogInformation(">>>>>>>>>>>>FIELD VISIBILITY: ConditionalState is null for field '{FieldId}' - checking if field has conditional logic", fieldId);
                // If no conditional state but field has conditional logic rules, hide it by default
                if (Template?.ConditionalLogic != null && HasFieldConditionalLogic(fieldId))
                {
                    _logger.LogInformation(">>>>>>>>>>>>FIELD VISIBILITY: Field '{FieldId}' has conditional logic but no state - hiding by default", fieldId);
                    return true;
                }
                return false;
            }

            if (ConditionalState.FieldVisibility.TryGetValue(fieldId, out var isVisible))
            {
                _logger.LogInformation(">>>>>>>>>>>>FIELD VISIBILITY: Field '{FieldId}' found with visibility={IsVisible}, returning hidden={IsHidden}", 
                    fieldId, isVisible, !isVisible);
                return !isVisible;
            }
            
            // Check if field has conditional logic rules - if so, hide by default until conditions are met
            if (Template?.ConditionalLogic != null && HasFieldConditionalLogic(fieldId))
            {
                _logger.LogInformation(">>>>>>>>>>>>FIELD VISIBILITY: Field '{FieldId}' has conditional logic but not evaluated - hiding by default", fieldId);
                return true;
            }
            
            _logger.LogInformation(">>>>>>>>>>>>FIELD VISIBILITY: Field '{FieldId}' not found and no conditional logic - defaulting to visible", fieldId);
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
            _logger.LogInformation(">>>>>>>>>>>>PAGE VISIBILITY CHECK: Checking page '{PageId}'", pageId);
            
            if (ConditionalState == null)
            {
                _logger.LogInformation(">>>>>>>>>>>>PAGE VISIBILITY: ConditionalState is null for page '{PageId}' - checking if page has conditional logic", pageId);
                // If no conditional state but page has conditional logic rules, hide it by default
                if (Template?.ConditionalLogic != null && HasPageConditionalLogic(pageId))
                {
                    _logger.LogInformation(">>>>>>>>>>>>PAGE VISIBILITY: Page '{PageId}' has conditional logic but no state - hiding by default", pageId);
                    return true;
                }
                return false;
            }

            // Check if page is in skipped list
            if (ConditionalState.SkippedPages.Contains(pageId))
            {
                _logger.LogInformation(">>>>>>>>>>>>PAGE VISIBILITY: Page '{PageId}' is in SkippedPages list - hidden", pageId);
                return true;
            }

            // Check if page is hidden by visibility rules
            if (ConditionalState.PageVisibility.TryGetValue(pageId, out var isVisible) && !isVisible)
            {
                _logger.LogInformation(">>>>>>>>>>>>PAGE VISIBILITY: Page '{PageId}' found with visibility={IsVisible} - hidden", pageId, isVisible);
                return true;
            }
            
            // NEW: Check if page has conditional logic rules and evaluate them
            if (Template?.ConditionalLogic != null && HasPageConditionalLogic(pageId))
            {
                // Check if any show rules for this page are triggered
                var hasShowRuleMet = Template.ConditionalLogic.Any(rule => 
                    rule.Enabled && 
                    rule.AffectedElements.Any(element => 
                        element.ElementId == pageId && 
                        element.ElementType == "page" && 
                        element.Action == "show") &&
                    EvaluateRuleConditions(rule));
                
                if (hasShowRuleMet)
                {
                    _logger.LogInformation(">>>>>>>>>>>>PAGE VISIBILITY: Page '{PageId}' has show rule that is met - visible", pageId);
                    return false; // Show the page
                }
                
                // Check if any hide/skip rules for this page are triggered
                var hasHideRuleMet = Template.ConditionalLogic.Any(rule => 
                    rule.Enabled && 
                    rule.AffectedElements.Any(element => 
                        element.ElementId == pageId && 
                        element.ElementType == "page" && 
                        (element.Action == "hide" || element.Action == "skip")) &&
                    EvaluateRuleConditions(rule));
                
                if (hasHideRuleMet)
                {
                    _logger.LogInformation(">>>>>>>>>>>>PAGE VISIBILITY: Page '{PageId}' has hide/skip rule that is met - hidden", pageId);
                    return true; // Hide the page
                }
                
                _logger.LogInformation(">>>>>>>>>>>>PAGE VISIBILITY: Page '{PageId}' has conditional logic but no rules are met - hiding by default", pageId);
                return true; // Hide by default if page has conditional logic but no rules match
            }
            
            _logger.LogInformation(">>>>>>>>>>>>PAGE VISIBILITY: Page '{PageId}' not found and no conditional logic - defaulting to visible", pageId);
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
                    _logger.LogInformation(">>>>>>>>>>>>CONDITIONAL TRIGGER: Rule '{RuleId}' conditions are met", rule.Id);
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
                
                _logger.LogInformation(">>>>>>>>>>>>CONDITIONAL SHOW CHECK: Checking rule '{RuleId}' for show page actions", rule.Id);
                
                if (EvaluateRuleConditions(rule))
                {
                    _logger.LogInformation(">>>>>>>>>>>>CONDITIONAL SHOW CHECK: Rule '{RuleId}' with show page action is met", rule.Id);
                    return true;
                }
            }
            
            _logger.LogInformation(">>>>>>>>>>>>CONDITIONAL SHOW CHECK: No rules with show page actions are met");
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
            Console.WriteLine($"[UPLOAD DEBUG] ========== OnPostUploadFileAsync START ==========");
            
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
                Console.WriteLine("[UPLOAD DEBUG] Initialized form engine context for POST (CurrentTask/Data ready)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UPLOAD DEBUG] CommonFormEngineInitializationAsync failed: {ex.Message}");
            }
            
            // Extract form data
            var applicationId = Request.Form["ApplicationId"].ToString();
            var fieldId = Request.Form["FieldId"].ToString();
            var returnUrl = Request.Form["ReturnUrl"].ToString();
            var uploadName = Request.Form["UploadName"].ToString();
            var uploadDescription = Request.Form["UploadDescription"].ToString();
            
            Console.WriteLine($"[UPLOAD DEBUG] ApplicationId: '{applicationId}'");
            Console.WriteLine($"[UPLOAD DEBUG] FieldId: '{fieldId}'");
            Console.WriteLine($"[UPLOAD DEBUG] ReturnUrl: '{returnUrl}'");
            Console.WriteLine($"[UPLOAD DEBUG] IsCollectionFlow: {IsCollectionFlow}");
            
            // Clear validation errors for FlowId/InstanceId if not in collection flow
            if (!IsCollectionFlow)
            {
                ModelState.Remove("FlowId");
                ModelState.Remove("InstanceId");
                Console.WriteLine($"[UPLOAD DEBUG] Cleared FlowId/InstanceId validation errors");
            }
            
            // Parse application ID
            if (!Guid.TryParse(applicationId, out var appId))
            {
                Console.WriteLine($"[UPLOAD DEBUG] Invalid ApplicationId: {applicationId}");
                return NotFound();
            }
            
            // Get uploaded file
            var file = Request.Form.Files["UploadFile"];
            // Read any existing file IDs posted by the view to preserve list
            var existingFileIds = Request.Form["ExistingFileIds"].ToArray();
            Console.WriteLine($"[UPLOAD DEBUG] File check - null: {file == null}, length: {file?.Length ?? 0}");
            
            // === EXACT REPLICA OF ORIGINAL ERROR HANDLING ===
            if (file == null || file.Length == 0)
            {
                Console.WriteLine($"[UPLOAD DEBUG] *** FILE VALIDATION FAILED ***");
                
                ErrorMessage = "Please select a file to upload.";
                ModelState.AddModelError("UploadFile", ErrorMessage);
                
                Console.WriteLine($"[UPLOAD DEBUG] Set ErrorMessage: '{ErrorMessage}'");
                Console.WriteLine($"[UPLOAD DEBUG] Added ModelState error for 'UploadFile'");
                Console.WriteLine($"[UPLOAD DEBUG] ModelState.IsValid: {ModelState.IsValid}");
                Console.WriteLine($"[UPLOAD DEBUG] ModelState.ErrorCount: {ModelState.ErrorCount}");
                
                // Debug all ModelState errors
                Console.WriteLine($"[UPLOAD DEBUG] === ALL MODEL STATE ERRORS ===");
                foreach (var kvp in ModelState)
                {
                    foreach (var error in kvp.Value.Errors)
                    {
                        Console.WriteLine($"[UPLOAD DEBUG] ModelState[{kvp.Key}]: {error.ErrorMessage}");
                    }
                }
                Console.WriteLine($"[UPLOAD DEBUG] === END MODEL STATE ERRORS ===");
                
                // CRITICAL: Save errors to FormErrorStore like original implementation
                // Note: API errors will be handled by ExternalApiExceptionFilter with FormErrorStore
                Console.WriteLine($"[UPLOAD DEBUG] Non-API validation errors - handling locally");
                
                // Load existing files (CRITICAL - exactly like original)
                Console.WriteLine($"[UPLOAD DEBUG] Loading existing files...");
                Files = await GetFilesForFieldAsync(appId, fieldId);
                Console.WriteLine($"[UPLOAD DEBUG] Loaded {Files.Count} existing files");
                
                // Check if we have return URL
                if (!string.IsNullOrEmpty(returnUrl))
                {
                    Console.WriteLine($"[UPLOAD DEBUG] *** REDIRECTING TO: {returnUrl} ***");
                    return Redirect(returnUrl);
                }
                
                Console.WriteLine($"[UPLOAD DEBUG] *** RETURNING PAGE() WITH ERRORS ***");
                Console.WriteLine($"[UPLOAD DEBUG] ErrorMessage property: '{ErrorMessage}'");
                Console.WriteLine($"[UPLOAD DEBUG] Files property count: {Files.Count}");
                Console.WriteLine($"[UPLOAD DEBUG] Template property null: {Template == null}");
                return Page();
            }
            
            // Continue with successful upload - let filter handle API errors
            Console.WriteLine($"[UPLOAD DEBUG] File validation passed, proceeding with upload");
            Console.WriteLine($"[UPLOAD DEBUG] *** ABOUT TO CALL API - NO TRY-CATCH ***");
            Console.WriteLine($"[UPLOAD DEBUG] *** ExternalApiExceptionFilter should catch any API errors ***");
            
            using var stream = file.OpenReadStream();
            var fileParam = new FileParameter(stream, file.FileName, file.ContentType);
            
            Console.WriteLine($"[UPLOAD DEBUG] *** CALLING UploadFileAsync API ***");
            try
            {
                await fileUploadService.UploadFileAsync(appId, file.FileName, uploadDescription, fileParam);
                Console.WriteLine($"[UPLOAD DEBUG] *** API CALL COMPLETED SUCCESSFULLY - NO EXCEPTION ***");
                
                // SUCCESS PATH: Only execute this code if API call succeeds
                Console.WriteLine($"[UPLOAD DEBUG] *** ENTERING SUCCESS PATH ***");
                
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
                        Console.WriteLine($"[UPLOAD DEBUG] Failed to merge ExistingFileIds: {ex.Message}");
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
                
                // Set success message
                SuccessMessage = $"Your file '{file.FileName}' uploaded.";
                Console.WriteLine($"[UPLOAD DEBUG] Upload successful: {SuccessMessage}");
                
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
                Console.WriteLine($"[UPLOAD DEBUG] Notification sent");
                
                // Redirect back if we have return URL
                if (!string.IsNullOrEmpty(returnUrl))
                {
                    Console.WriteLine($"[UPLOAD DEBUG] Redirecting to return URL: {returnUrl}");
                    return Redirect(returnUrl);
                }
                
                Console.WriteLine($"[UPLOAD DEBUG] Upload successful, returning Page()");
                return Page();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UPLOAD DEBUG] *** EXCEPTION CAUGHT IN UPLOAD METHOD ***");
                Console.WriteLine($"[UPLOAD DEBUG] *** Exception: {ex.GetType().Name} - {ex.Message} ***");
                Console.WriteLine($"[UPLOAD DEBUG] *** RETHROWING FOR FILTER TO HANDLE ***");
                // Don't handle the exception here - let the ExternalApiExceptionFilter handle it
                // This ensures that API errors get proper ModelState treatment
                throw;
            }
        }

        public async Task<IActionResult> OnPostDownloadFileAsync()
        {
            Console.WriteLine($"[UPLOAD DEBUG] Download handler called in RenderForm");
            
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
                Console.WriteLine($"[UPLOAD DEBUG] Created empty template to prevent null reference for download");
            }
            
            var applicationId = Request.Form["ApplicationId"].ToString();
            var fileIdStr = Request.Form["FileId"].ToString();
            
            if (!Guid.TryParse(applicationId, out var appId))
                return NotFound();
            if (!Guid.TryParse(fileIdStr, out var fileId))
                return NotFound();

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
            Console.WriteLine($"[UPLOAD DEBUG] Delete handler called in RenderForm");
            
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
                Console.WriteLine($"[UPLOAD DEBUG] Created empty template to prevent null reference for delete");
            }
            
            // CRITICAL: Setup notification request for delete operations
            var fieldId = Request.Form["FieldId"].ToString();
            var addRequest = new AddNotificationRequest
            {
                Message = string.Empty, // set later when known
                Category = "file-upload",
                Context = fieldId + "FileDeletion",
                Type = NotificationType.Success
            };
            
            var applicationId = Request.Form["ApplicationId"].ToString();
            var returnUrl = Request.Form["ReturnUrl"].ToString();
            var fileIdStr = Request.Form["FileId"].ToString();
            
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

            await fileUploadService.DeleteFileAsync(fileId, appId);
            Console.WriteLine($"[UPLOAD DEBUG] File deleted successfully: {fileId}");
            
            // CRITICAL: Set success message for delete operation
            SuccessMessage = "File deleted.";
            Console.WriteLine($"[UPLOAD DEBUG] File delete completed successfully. Success message: {SuccessMessage}");

            // Get current files for this field and remove the deleted one
            var currentFieldFiles = (await GetFilesForFieldAsync(appId, fieldId)).ToList();
            currentFieldFiles.RemoveAll(f => f.Id == fileId);
            
            Console.WriteLine($"[UPLOAD DEBUG] Files after deletion: {currentFieldFiles.Count}");
            
            UpdateSessionFileList(appId, fieldId, currentFieldFiles);
            await SaveUploadedFilesToResponseAsync(appId, fieldId, currentFieldFiles);
            
            // If we have a return URL (from partial form), redirect back
            if (!string.IsNullOrEmpty(returnUrl))
            {
                // CRITICAL: Send notification for successful delete
                addRequest.Message = SuccessMessage;
                await _notificationsClient.CreateNotificationAsync(addRequest);
                Console.WriteLine($"[UPLOAD DEBUG] Delete notification sent: {SuccessMessage}");
                
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

            Console.WriteLine($"[UPLOAD DEBUG] ========== LOADING FILES FOR FIELD START ==========");
            Console.WriteLine($"[UPLOAD DEBUG] FlowId: {FlowId}, InstanceId: {InstanceId}, FieldId: {fieldId}");

            if (IsCollectionFlow)
            {
                Console.WriteLine($"[UPLOAD DEBUG] Collection flow - using same data access as page load");

                // CRITICAL FIX: Scan accumulated collection items to find this instance's item,
                // then read the inner field value (matching 'fieldId') like GET does.
                try
                {
                    var accumulatedData = applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
                    Console.WriteLine($"[UPLOAD DEBUG] Accumulated data keys: {string.Join(", ", accumulatedData.Keys)}");

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
                                Console.WriteLine($"[UPLOAD DEBUG] Found existing item for instance {InstanceId} under collection key '{kvp.Key}'");
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
                                                Console.WriteLine($"[UPLOAD DEBUG] Loaded {files.Count} files from existing item (JsonElement)");
                                                return files.AsReadOnly();
                                            }
                                            catch (JsonException ex)
                                            {
                                                Console.WriteLine($"[UPLOAD DEBUG] Failed to deserialize inner files (JsonElement): {ex.Message}");
                                            }
                                        }
                                    }
                                    // Handle string JSON
                                    else if (innerValue is string innerJson && !string.IsNullOrWhiteSpace(innerJson))
                                    {
                                        try
                                        {
                                            var files = JsonSerializer.Deserialize<List<UploadDto>>(innerJson) ?? new List<UploadDto>();
                                            Console.WriteLine($"[UPLOAD DEBUG] Loaded {files.Count} files from existing item (string)");
                                            return files.AsReadOnly();
                                        }
                                        catch (JsonException ex)
                                        {
                                            Console.WriteLine($"[UPLOAD DEBUG] Failed to deserialize inner files (string): {ex.Message}");
                                        }
                                    }
                                    // Handle direct list
                                    else if (innerValue is List<UploadDto> uploadList)
                                    {
                                        Console.WriteLine($"[UPLOAD DEBUG] Loaded {uploadList.Count} files from existing item (typed list)");
                                        return uploadList.AsReadOnly();
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Ignore parse errors for non-collection fields
                            Console.WriteLine($"[UPLOAD DEBUG] Skipping non-collection accumulated key '{kvp.Key}': {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UPLOAD DEBUG] Error scanning accumulated data: {ex.Message}");
                }

                // FALLBACK: Check session flow progress
                Console.WriteLine($"[UPLOAD DEBUG] Falling back to session flow progress search");
                var progressData = LoadFlowProgress(FlowId, InstanceId);
                Console.WriteLine($"[UPLOAD DEBUG] Progress data keys: {string.Join(", ", progressData.Keys)}");

                if (progressData.TryGetValue(fieldId, out var progressValue))
                {
                    var sessionFilesJson = progressValue?.ToString();
                    Console.WriteLine($"[UPLOAD DEBUG] Found files in flow progress: {sessionFilesJson?.Substring(0, Math.Min(200, sessionFilesJson?.Length ?? 0))}...");

                    if (!string.IsNullOrWhiteSpace(sessionFilesJson))
                    {
                        try
                        {
                            var files = JsonSerializer.Deserialize<List<UploadDto>>(sessionFilesJson);
                            Console.WriteLine($"[UPLOAD DEBUG] Successfully deserialized {files?.Count ?? 0} files from session");
                            return (files ?? new List<UploadDto>()).AsReadOnly();
                        }
                        catch (JsonException ex)
                        {
                            Console.WriteLine($"[UPLOAD DEBUG] Failed to deserialize files from session: {ex.Message}");
                        }
                    }
                }
            }
            else
            {
                // For regular forms, get files from session
                var sessionKey = $"UploadedFiles_{appId}_{fieldId}";
                var sessionFilesJson = HttpContext.Session.GetString(sessionKey);
                Console.WriteLine($"[UPLOAD DEBUG] Session files JSON from regular session: {sessionFilesJson?.Substring(0, Math.Min(200, sessionFilesJson?.Length ?? 0))}...");

                if (!string.IsNullOrWhiteSpace(sessionFilesJson))
                {
                    try
                    {
                        var files = JsonSerializer.Deserialize<List<UploadDto>>(sessionFilesJson) ?? new List<UploadDto>();
                        Console.WriteLine($"[UPLOAD DEBUG] Successfully deserialized {files.Count} files from regular session");
                        return files.AsReadOnly();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[UPLOAD DEBUG] Error deserializing files from regular session: {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"[UPLOAD DEBUG] No files found anywhere, returning empty list");
            return new List<UploadDto>().AsReadOnly();
        }

        private void UpdateSessionFileList(Guid appId, string fieldId, IReadOnlyList<UploadDto> files)
        {
            Console.WriteLine($"[UPLOAD DEBUG] ========== UPDATE SESSION FILE LIST START ==========");
            Console.WriteLine($"[UPLOAD DEBUG] UpdateSessionFileList called with {files.Count} files for field {fieldId}");
            foreach (var file in files)
            {
                Console.WriteLine($"[UPLOAD DEBUG] Storing file: {file.OriginalFileName} (ID: {file.Id})");
            }
            
            if (IsCollectionFlow)
            {
                // For collection flows, store in flow progress system
                var progressKey = GetFlowProgressSessionKey(FlowId, InstanceId);
                Console.WriteLine($"[UPLOAD DEBUG] Storing in flow progress with key: {progressKey}");
                Console.WriteLine($"[UPLOAD DEBUG] Session ID in UpdateSessionFileList: {HttpContext.Session.Id}");
                
                // Debug: List all session keys before calling LoadFlowProgress
                var sessionKeysBefore = HttpContext.Session.Keys.ToList();
                Console.WriteLine($"[UPLOAD DEBUG] Session keys BEFORE LoadFlowProgress: {string.Join(", ", sessionKeysBefore)}");
                
                // CRITICAL FIX: Use same method as page load for consistency
                var existingProgress = LoadFlowProgress(FlowId, InstanceId);
                Console.WriteLine($"[UPLOAD DEBUG] LoadFlowProgress returned {existingProgress.Keys.Count} keys: {string.Join(", ", existingProgress.Keys)}");
                
                // CRITICAL FIX: The 'files' parameter contains ALL files (existing + new), so just save it directly
                // No need to merge because GetFilesForFieldAsync already combined existing and new files
                var serializedFiles = JsonSerializer.Serialize(files);
                Console.WriteLine($"[UPLOAD DEBUG] Serialized ALL files (existing + new): {serializedFiles.Substring(0, Math.Min(200, serializedFiles.Length))}...");
                existingProgress[fieldId] = serializedFiles;
                
                // Force session to commit immediately
                var progressJson = JsonSerializer.Serialize(existingProgress);
                HttpContext.Session.SetString(progressKey, progressJson);
                
                Console.WriteLine($"[UPLOAD DEBUG] Saved flow progress with {existingProgress.Keys.Count} keys");
                
                // Flow progress saved successfully
                Console.WriteLine($"[UPLOAD DEBUG] Flow progress saved successfully");
            }
            else
            {
                // For regular forms, use the original session key
                var key = $"UploadedFiles_{appId}_{fieldId}";
                Console.WriteLine($"[UPLOAD DEBUG] Storing in regular session with key: {key}");
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

    }
}







