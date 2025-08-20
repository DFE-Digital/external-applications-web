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
        ILogger<RenderFormModel> logger)
        : BaseFormEngineModel(renderer, applicationResponseService, fieldFormattingService, templateManagementService,
            applicationStateService, formStateManager, formNavigationService, formDataManager, formValidationOrchestrator, formConfigurationService, logger)
    {
        private readonly IApplicationsClient _applicationsClient = applicationsClient;

        [BindProperty] public Dictionary<string, object> Data { get; set; } = new();

        [BindProperty] public bool IsTaskCompleted { get; set; }

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
                        // Sub-flow: initialize task and resolve page from flow definition
                        var (group, task) = InitializeCurrentTask(TaskId);
                        CurrentGroup = group;
                        CurrentTask = task;

                        var flow = _templateManagementService.FindFlow(Template, flowId);
                        if (flow != null)
                        {
                            var page = string.IsNullOrEmpty(flowPageId) ? flow.Pages.FirstOrDefault() : _templateManagementService.FindFlowPage(flow, flowPageId);
                            if (page != null)
                            {
                                CurrentPage = page;
                                CurrentFormState = FormState.FormPage; // Render as a normal page
                                
                                // If editing existing item, load its data into form fields
                                // This must happen AFTER LoadAccumulatedDataFromSession is skipped for sub-flows
                                LoadExistingFlowItemData(flowId, instanceId);
                                
                                // Also load any in-progress data for this specific flow instance
                                var progressData = LoadFlowProgress(flowId, instanceId);
                                foreach (var kvp in progressData)
                                {
                                    if (!Data.ContainsKey(kvp.Key)) // Don't overwrite data from existing item
                                    {
                                        Data[kvp.Key] = kvp.Value;
                                    }
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
                }
            }

            // Check if we need to clear session data for a new application
            CheckAndClearSessionForNewApplication();

            // Load accumulated form data from session to pre-populate fields (only if not in a sub-flow)
            if (string.IsNullOrEmpty(CurrentPageId) || !CurrentPageId.Contains("flow/"))
            {
                LoadAccumulatedDataFromSession();
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

            // Handle task completion if checkbox is checked
            if (IsTaskCompleted && CurrentTask != null && ApplicationId.HasValue)
            {
                // Mark the task as completed in session and API
                await _applicationStateService.SaveTaskStatusAsync(ApplicationId.Value, CurrentTask.TaskId, Domain.Models.TaskStatus.Completed, HttpContext.Session);
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

                    var flow = _templateManagementService.FindFlow(Template, flowId);
                    if (flow != null)
                    {
                        var page = string.IsNullOrEmpty(flowPageId) ? flow.Pages.FirstOrDefault() : _templateManagementService.FindFlowPage(flow, flowPageId);
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
                    var flow = _templateManagementService.FindFlow(Template, flowId);
                    if (flow != null)
                    {
                        // Persist in-progress sub-flow data for this instance
                        SaveFlowProgress(flowId, instanceId, Data);

                        var index = flow.Pages.FindIndex(p => p.PageId == CurrentPage.PageId);
                        var isLast = index == -1 || index >= flow.Pages.Count - 1;
                        if (!isLast)
                        {
                            var nextPageId = flow.Pages[index + 1].PageId;
                            var nextUrl = _formNavigationService.GetSubFlowPageUrl(CurrentTask.TaskId, ReferenceNumber, flowId, instanceId, nextPageId);
                            return Redirect(nextUrl);
                        }
                        else
                        {
                            // Flow complete: append item to collection and go back to collection summary
                            var fieldId = CurrentTask.Summary?.FieldId ?? string.Empty;
                            if (!string.IsNullOrEmpty(fieldId))
                            {
                                // Merge accumulated progress with final page data
                                var accumulated = LoadFlowProgress(flowId, instanceId);
                                foreach (var kv in Data)
                                {
                                    accumulated[kv.Key] = kv.Value;
                                }

                                AppendCollectionItemToSession(flow, fieldId, instanceId, accumulated);
                                if (ApplicationId.HasValue)
                                {
                                    // Trigger save for the collection field
                                    var acc = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
                                    if (acc.TryGetValue(fieldId, out var collectionValue))
                                    {
                                        await _applicationResponseService.SaveApplicationResponseAsync(ApplicationId.Value, new Dictionary<string, object> { [fieldId] = collectionValue }, HttpContext.Session);
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

        public async Task<IActionResult> OnPostRemoveCollectionItemAsync(string fieldId, string itemId)
        {
            await CommonFormEngineInitializationAsync();
            
            if (string.IsNullOrEmpty(fieldId) || string.IsNullOrEmpty(itemId))
            {
                return BadRequest("Field ID and Item ID are required");
            }

            // Get current collection from session
            var accumulated = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
            if (accumulated.TryGetValue(fieldId, out var collectionValue))
            {
                var json = collectionValue?.ToString() ?? "[]";
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

        private void AppendCollectionItemToSession(FlowDefinition flow, string fieldId, string instanceId, Dictionary<string, object> itemData)
        {
            // Merge Data dictionary (captured for the flow pages) into a single item object
            var item = new Dictionary<string, object>();
            foreach (var page in flow.Pages)
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

            // Upsert by id
            var idx = list.FindIndex(x => x.TryGetValue("id", out var id) && id?.ToString() == instanceId);
            if (idx >= 0) list[idx] = item; else list.Add(item);

            var serialized = JsonSerializer.Serialize(list);
            _applicationResponseService.AccumulateFormData(new Dictionary<string, object> { [fieldId] = serialized }, HttpContext.Session);
        }

        private static string GetFlowProgressSessionKey(string flowId, string instanceId) => $"FlowProgress_{flowId}_{instanceId}";

        private Dictionary<string, object> LoadFlowProgress(string flowId, string instanceId)
        {
            var key = GetFlowProgressSessionKey(flowId, instanceId);
            var json = HttpContext.Session.GetString(key);
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object>();
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
            var fieldId = task?.Summary?.FieldId;
            
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


    }
}







