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
        ILogger<RenderFormModel> logger)
        : BaseFormEngineModel(renderer, applicationResponseService, fieldFormattingService, templateManagementService,
            applicationStateService, formStateManager, formNavigationService, formDataManager, formValidationOrchestrator, formConfigurationService, logger)
    {
        private readonly IApplicationsClient _applicationsClient = applicationsClient;
        private readonly IConditionalLogicOrchestrator _conditionalLogicOrchestrator = conditionalLogicOrchestrator;

        [BindProperty] public Dictionary<string, object> Data { get; set; } = new();

        [BindProperty] public bool IsTaskCompleted { get; set; }

        // Success message for collection operations
        [TempData] public string? SuccessMessage { get; set; }

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
                                foreach (var kv in Data)
                                {
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
                            }
                            var backToSummary = _formNavigationService.GetCollectionFlowSummaryUrl(CurrentTask.TaskId, ReferenceNumber);
                            return Redirect(backToSummary);
                        }
                    }
                }
                else
                {
                    var nextUrl = _formNavigationService.GetNextNavigationTargetAfterSave(CurrentPage, CurrentTask, ReferenceNumber);
                    return Redirect(nextUrl);
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

            // Upsert the item
            if (idx >= 0) 
                list[idx] = item; 
            else 
                list.Add(item);

            var serialized = JsonSerializer.Serialize(list);
            _applicationResponseService.AccumulateFormData(new Dictionary<string, object> { [fieldId] = serialized }, HttpContext.Session);
        }

        private static string GetFlowProgressSessionKey(string flowId, string instanceId) => $"FlowProgress_{flowId}_{instanceId}";

        private Dictionary<string, object> LoadFlowProgress(string flowId, string instanceId)
        {
            var key = GetFlowProgressSessionKey(flowId, instanceId);
            var json = HttpContext.Session.GetString(key);
            if (string.IsNullOrWhiteSpace(json)) 
            {
                _logger.LogInformation(">>>>>>>>>>>>FLOW PROGRESS LOAD: flowId={FlowId}, instanceId={InstanceId}, result=EMPTY", flowId, instanceId);
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
                _logger.LogInformation(">>>>>>>>>>>>CONDITIONAL LOGIC START: CurrentPageId={CurrentPageId}, TaskId={TaskId}, Template has {RuleCount} rules, Trigger={Trigger}", 
                    CurrentPageId, TaskId, Template?.ConditionalLogic?.Count ?? 0, trigger);
                _logger.LogInformation(">>>>>>>>>>>>CURRENT DATA: {Data}", 
                    string.Join(", ", Data.Select(kv => $"{kv.Key}={kv.Value}")));

                if (Template?.ConditionalLogic != null && Template.ConditionalLogic.Any())
                {
                    // Log all rules in template
                    foreach (var rule in Template.ConditionalLogic)
                    {
                        _logger.LogInformation(">>>>>>>>>>>>RULE: {RuleId} - {RuleName}, Priority: {Priority}, Enabled: {Enabled}, ExecuteOn: [{ExecuteOn}]", 
                            rule.Id, rule.Name, rule.Priority, rule.Enabled, string.Join(", ", rule.ExecuteOn));
                        foreach (var condition in rule.ConditionGroup.Conditions)
                        {
                            _logger.LogInformation(">>>>>>>>>>>>  CONDITION: {TriggerField} {Operator} {Value} (Current Value: {CurrentValue})", 
                                condition.TriggerField, condition.Operator, condition.Value, 
                                Data.TryGetValue(condition.TriggerField, out var currentVal) ? currentVal : "NOT_SET");
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
                    
                    _logger.LogInformation(">>>>>>>>>>>>CONDITIONAL LOGIC RESULT:");
                    _logger.LogInformation(">>>>>>>>>>>>  FieldVisibility: {FieldVisibility}", 
                        string.Join(", ", ConditionalState.FieldVisibility.Select(kv => $"{kv.Key}={kv.Value}")));
                    _logger.LogInformation(">>>>>>>>>>>>  SkippedPages: {SkippedPages}", 
                        string.Join(", ", ConditionalState.SkippedPages));
                    _logger.LogInformation(">>>>>>>>>>>>  Actions Executed: {ActionCount}", 
                        ConditionalState.EvaluationResult?.Actions.Count ?? 0);
                    
                    // Apply field values from conditional logic
                    if (ConditionalState.FieldValues.Any())
                    {
                        foreach (var kvp in ConditionalState.FieldValues)
                        {
                            Data[kvp.Key] = kvp.Value;
                        }
                        _logger.LogInformation(">>>>>>>>>>>>  Applied FieldValues: {FieldValues}", 
                            string.Join(", ", ConditionalState.FieldValues.Select(kv => $"{kv.Key}={kv.Value}")));
                    }
                }
                else
                {
                    _logger.LogInformation(">>>>>>>>>>>>CONDITIONAL LOGIC: No rules found in template");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ">>>>>>>>>>>>CONDITIONAL LOGIC ERROR: {Message}", ex.Message);
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
                _logger.LogInformation(">>>>>>>>>>>>FIELD VISIBILITY: ConditionalState is null for field '{FieldId}' - defaulting to visible", fieldId);
                return false;
            }

            if (ConditionalState.FieldVisibility.TryGetValue(fieldId, out var isVisible))
            {
                _logger.LogInformation(">>>>>>>>>>>>FIELD VISIBILITY: Field '{FieldId}' found with visibility={IsVisible}, returning hidden={IsHidden}", 
                    fieldId, isVisible, !isVisible);
                return !isVisible;
            }
            
            _logger.LogInformation(">>>>>>>>>>>>FIELD VISIBILITY: Field '{FieldId}' not found in FieldVisibility dictionary (has {Count} entries) - defaulting to visible", 
                fieldId, ConditionalState.FieldVisibility.Count);
            _logger.LogInformation(">>>>>>>>>>>>FIELD VISIBILITY: Available fields: {AvailableFields}", 
                string.Join(", ", ConditionalState.FieldVisibility.Keys));
            return false;
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

    }
}







