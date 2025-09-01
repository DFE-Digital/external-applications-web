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
        IConfirmationService confirmationService,
        ILogger<RenderFormModel> logger)
        : BaseFormEngineModel(renderer, applicationResponseService, fieldFormattingService, templateManagementService,
            applicationStateService, formStateManager, formNavigationService, formDataManager, formValidationOrchestrator, formConfigurationService, logger)
    {
        private readonly IApplicationsClient _applicationsClient = applicationsClient;
        private readonly IConfirmationService _confirmationService = confirmationService;

        [BindProperty] public Dictionary<string, object> Data { get; set; } = new();

        [BindProperty] public bool IsTaskCompleted { get; set; }
        
        // Success message for collection operations
        [TempData] public string? SuccessMessage { get; set; }

        public async Task OnGetAsync()
        {
            try
            {
                // Log session state before initialization
                _logger.LogInformation("OnGetAsync: Starting - ReferenceNumber: {ReferenceNumber}, TaskId: {TaskId}, CurrentPageId: {CurrentPageId}, SessionKeys: {SessionKeys}", 
                    ReferenceNumber, TaskId, CurrentPageId, string.Join(", ", HttpContext.Session.Keys));
                
                // Log template ID from session before initialization
                var sessionTemplateId = HttpContext.Session.GetString("TemplateId");
                _logger.LogInformation("OnGetAsync: Session TemplateId before initialization: {SessionTemplateId}", sessionTemplateId);
                
                // Log session ID to track session continuity
                var sessionId = HttpContext.Session.Id;
                _logger.LogInformation("OnGetAsync: Session ID: {SessionId}", sessionId);
                
                // Log the full request URL for debugging
                var fullUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
                _logger.LogInformation("OnGetAsync: Full request URL: {FullUrl}", fullUrl);
                
                // Log route parsing attempts
                if (!string.IsNullOrEmpty(CurrentPageId))
                {
                    _logger.LogInformation("OnGetAsync: Attempting to parse CurrentPageId: {CurrentPageId}", CurrentPageId);
                    
                    if (TryParseConfirmationRoute(CurrentPageId, out var operation, out var fieldId, out var confirmationToken))
                    {
                        _logger.LogInformation("OnGetAsync: Confirmation route detected - Operation: {Operation}, Field: {FieldId}, Token: {Token}", 
                            operation, fieldId, confirmationToken);
                    }
                    else
                    {
                        _logger.LogInformation("OnGetAsync: Not a confirmation route - CurrentPageId: {CurrentPageId}", CurrentPageId);
                    }
                }
                
                await CommonFormEngineInitializationAsync();

                // Log template loading status for debugging
                _logger.LogInformation("OnGetAsync: Template loaded - TemplateId: {TemplateId}, Template null: {TemplateNull}, CurrentPageId: {CurrentPageId}", 
                    TemplateId, Template == null, CurrentPageId);

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
                        else if (TryParseConfirmationRoute(CurrentPageId, out var operation, out var fieldId, out var confirmationToken))
                        {
                            // Confirmation route: ensure template is loaded and initialize task
                            _logger.LogInformation("Processing confirmation route - Operation: {Operation}, Field: {FieldId}, Token: {Token}, Template null: {TemplateNull}", 
                                operation, fieldId, confirmationToken, Template == null);
                            
                            // Ensure template is loaded for confirmation routes
                            if (Template == null)
                            {
                                _logger.LogWarning("Template is null for confirmation route, attempting to reload");
                                
                                // Try multiple approaches to load the template
                                bool templateLoaded = false;
                                
                                // First, try to reload using the current TemplateId
                                if (!string.IsNullOrEmpty(TemplateId))
                                {
                                    try
                                    {
                                        _logger.LogInformation("Attempting to reload template with existing TemplateId: {TemplateId}", TemplateId);
                                        await LoadTemplateAsync();
                                        if (Template != null)
                                        {
                                            templateLoaded = true;
                                            _logger.LogInformation("Template reloaded successfully using existing TemplateId");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "Failed to reload template with existing TemplateId: {TemplateId}", TemplateId);
                                    }
                                }
                                
                                // If that failed, try to get TemplateId from session and reload
                                if (!templateLoaded)
                                {
                                    try
                                    {
                                        var sessionTemplateIdValue = HttpContext.Session.GetString("TemplateId");
                                        if (!string.IsNullOrEmpty(sessionTemplateIdValue))
                                        {
                                            _logger.LogInformation("Attempting to reload template with session TemplateId: {SessionTemplateId}", sessionTemplateIdValue);
                                            TemplateId = sessionTemplateIdValue;
                                            await LoadTemplateAsync();
                                            if (Template != null)
                                            {
                                                templateLoaded = true;
                                                _logger.LogInformation("Template reloaded successfully using session TemplateId");
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "Failed to reload template with session TemplateId");
                                    }
                                }
                                
                                // If still no template, try to ensure application context and reload
                                if (!templateLoaded)
                                {
                                    try
                                    {
                                        _logger.LogInformation("Attempting to ensure application context and reload template");
                                        await EnsureApplicationIdAsync();
                                        await LoadTemplateAsync();
                                        if (Template != null)
                                        {
                                            templateLoaded = true;
                                            _logger.LogInformation("Template reloaded successfully after ensuring application context");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "Failed to reload template after ensuring application context");
                                    }
                                }
                                
                                // If still no template, try to force template loading from scratch
                                if (!templateLoaded)
                                {
                                    try
                                    {
                                        _logger.LogInformation("Attempting to force template loading from scratch");
                                        
                                        // Clear any existing template state
                                        Template = null;
                                        
                                        // Get template ID from session again
                                        var forceTemplateId = HttpContext.Session.GetString("TemplateId");
                                        if (!string.IsNullOrEmpty(forceTemplateId))
                                        {
                                            _logger.LogInformation("Force loading template with ID: {ForceTemplateId}", forceTemplateId);
                                            TemplateId = forceTemplateId;
                                            
                                            // Try to load template directly from service
                                            if (_templateManagementService != null)
                                            {
                                                Template = await _templateManagementService.LoadTemplateAsync(TemplateId, CurrentApplication);
                                                if (Template != null)
                                                {
                                                    templateLoaded = true;
                                                    _logger.LogInformation("Template force loaded successfully from service");
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "Failed to force load template");
                                    }
                                }
                                
                                // If all attempts failed, redirect to application list
                                if (!templateLoaded)
                                {
                                    _logger.LogError("All template loading attempts failed for confirmation route. Session keys: {SessionKeys}, TemplateId: {TemplateId}", 
                                        string.Join(", ", HttpContext.Session.Keys), TemplateId);
                                    Response.Redirect($"~/applications/{ReferenceNumber}");
                                    return;
                                }
                            }
                            
                            var (group, task) = InitializeCurrentTask(TaskId);
                            CurrentGroup = group;
                            CurrentTask = task;
                            CurrentPage = null; // No specific page for confirmation
                            CurrentFormState = FormState.Confirmation;
                            
                            // Load the pending form data for display on confirmation page instead of accumulated data
                            // This ensures we show the newly selected trust, not the old one
                            var pendingFormSubmissionJson = HttpContext.Session.GetString("PendingFormSubmission");
                            if (!string.IsNullOrEmpty(pendingFormSubmissionJson))
                            {
                                try
                                {
                                    var pendingData = JsonSerializer.Deserialize<Dictionary<string, object>>(pendingFormSubmissionJson);
                                    if (pendingData != null && pendingData.TryGetValue("FormData", out var formDataObj))
                                    {
                                        var formData = JsonSerializer.Deserialize<Dictionary<string, object>>(formDataObj.ToString() ?? "{}");
                                        if (formData != null)
                                        {
                                            // Clear existing data and load the pending form data
                                            Data.Clear();
                                            foreach (var kvp in formData)
                                            {
                                                // Transform Data_ prefixed keys to regular field IDs for the view
                                                var key = kvp.Key;
                                                if (key.StartsWith("Data_"))
                                                {
                                                    var transformedKey = key.Substring(5); // Remove "Data_" prefix
                                                    Data[transformedKey] = kvp.Value;
                                                }
                                                else
                                                {
                                                    Data[key] = kvp.Value;
                                                }
                                            }
                                            _logger.LogInformation("Loaded pending form data for confirmation page: {Count} fields", formData.Count);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to load pending form data for confirmation page, falling back to accumulated data");
                                    LoadAccumulatedDataFromSession();
                                }
                            }
                            else
                            {
                                _logger.LogWarning("No pending form submission data found, falling back to accumulated data");
                                LoadAccumulatedDataFromSession();
                            }
                            
                            _logger.LogInformation("Confirmation route initialized successfully - Operation: {Operation}, Field: {FieldId}, Task: {TaskId}", 
                                operation, fieldId, TaskId);
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

                // Load accumulated form data from session to pre-populate fields (only if not in a sub-flow or confirmation route)
                if ((string.IsNullOrEmpty(CurrentPageId) || !CurrentPageId.Contains("flow/")) && 
                    !CurrentPageId?.StartsWith("confirm/") == true)
                {
                    LoadAccumulatedDataFromSession();
                }

                // Initialize task completion status if we're showing a task summary
                if (CurrentFormState == FormState.TaskSummary && CurrentTask != null)
                {
                    var taskStatus = GetTaskStatusFromSession(CurrentTask.TaskId);
                    IsTaskCompleted = taskStatus == Domain.Models.TaskStatus.Completed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnGetAsync for reference {ReferenceNumber}, pageId {CurrentPageId}", ReferenceNumber, CurrentPageId);
                
                // Redirect to error page or back to application list
                Response.Redirect($"~/applications/{ReferenceNumber}");
                return;
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
                // Check if any fields require confirmation before saving
                var fieldsRequiringConfirmation = GetFieldsRequiringConfirmation(Data);
                if (fieldsRequiringConfirmation.Any())
                {
                    // Store the form data in session for after confirmation
                    var confirmationData = new
                    {
                        FormData = Data,
                        CurrentPageId = CurrentPageId,
                        TaskId = TaskId,
                        ReferenceNumber = ReferenceNumber
                    };
                    
                    HttpContext.Session.SetString("PendingFormSubmission", JsonSerializer.Serialize(confirmationData));
                    
                    // Redirect to confirmation page for the first field that requires confirmation
                    var firstField = fieldsRequiringConfirmation.First();
                    var confirmationModel = _confirmationService.CreateConfirmationModel(
                        firstField, 
                        ConfirmationOperation.Update, 
                        Data, 
                        TaskId, 
                        ReferenceNumber);

                    var confirmationUrl = _formNavigationService.GetConfirmationUrl(
                        TaskId, 
                        ReferenceNumber, 
                        "update", 
                        firstField.FieldId, 
                        confirmationModel.ConfirmationToken);

                    _logger.LogInformation("Redirecting to confirmation page - URL: {ConfirmationUrl}, Field: {FieldId}, Operation: update", 
                        confirmationUrl, firstField.FieldId);

                    return Redirect(confirmationUrl);
                }

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
                            var nextPageId = flowPages[index + 1].PageId;
                            var nextUrl = _formNavigationService.GetSubFlowPageUrl(CurrentTask.TaskId, ReferenceNumber, flowId, instanceId, nextPageId);
                            return Redirect(nextUrl);
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

        /// <summary>
        /// Handles POST requests for the confirmation page
        /// </summary>
        public async Task<IActionResult> OnPostAsync()
        {
            // Check if this is a confirmation form submission
            var operation = Request.Form["Operation"].ToString();
            var fieldId = Request.Form["FieldId"].ToString();
            var confirmationToken = Request.Form["ConfirmationToken"].ToString();
            
            _logger.LogInformation("OnPostAsync: Confirmation form submitted - Operation: {Operation}, Field: {FieldId}, Token: {Token}", 
                operation, fieldId, confirmationToken);
            
            // Route to the appropriate confirmation handler based on operation
            switch (operation?.ToLower())
            {
                case "update":
                    return await OnPostConfirmedUpdateAsync(fieldId);
                    
                case "delete":
                    // For delete operations, we need itemId which should be passed in the form
                    var itemId = Request.Form["ItemId"].ToString();
                    var flowId = Request.Form["FlowId"].ToString();
                    return await OnPostConfirmedDeleteAsync(fieldId, itemId, flowId);
                    
                case "add":
                    return await OnPostConfirmedAddAsync(fieldId);
                    
                default:
                    _logger.LogWarning("Unknown confirmation operation: {Operation}", operation);
                    return BadRequest($"Unknown confirmation operation: {operation}");
            }
        }

        /// <summary>
        /// Handles POST requests specifically for confirmation forms
        /// </summary>
        public async Task<IActionResult> OnPostConfirmationAsync()
        {
            _logger.LogInformation("OnPostConfirmationAsync: Method called");
            
            // Log all form data for debugging
            foreach (var key in Request.Form.Keys)
            {
                _logger.LogInformation("Form field: {Key} = {Value}", key, Request.Form[key].ToString());
            }
            
            // Check if this is a confirmation form submission
            var operation = Request.Form["Operation"].ToString();
            var fieldId = Request.Form["FieldId"].ToString();
            var confirmationToken = Request.Form["ConfirmationToken"].ToString();
            var userConfirmed = Request.Form["UserConfirmed"].ToString();
            
            _logger.LogInformation("OnPostConfirmationAsync: Extracted values - Operation: {Operation}, Field: {FieldId}, Token: {Token}, UserConfirmed: {UserConfirmed}", 
                operation, fieldId, confirmationToken, userConfirmed);
            
            // Validate required fields
            if (string.IsNullOrEmpty(operation))
            {
                _logger.LogError("Operation is missing from form data");
                return BadRequest("Operation is required");
            }
            
            if (string.IsNullOrEmpty(fieldId))
            {
                _logger.LogError("FieldId is missing from form data");
                return BadRequest("FieldId is required");
            }
            
            if (string.IsNullOrEmpty(userConfirmed))
            {
                _logger.LogError("UserConfirmed is missing from form data");
                return BadRequest("Please select Yes or No");
            }
            
            // Route to the appropriate confirmation handler based on operation
            switch (operation?.ToLower())
            {
                case "update":
                    return await OnPostConfirmedUpdateAsync(fieldId);
                    
                case "delete":
                    // For delete operations, we need itemId which should be passed in the form
                    var itemId = Request.Form["ItemId"].ToString();
                    var flowId = Request.Form["FlowId"].ToString();
                    return await OnPostConfirmedDeleteAsync(fieldId, itemId, flowId);
                    
                case "add":
                    return await OnPostConfirmedAddAsync(fieldId);
                    
                default:
                    _logger.LogWarning("Unknown confirmation operation: {Operation}", operation);
                    return BadRequest($"Unknown confirmation operation: {operation}");
            }
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
                }
            }

            // Check if confirmation is required for this field
            var field = FindFieldById(fieldId);
            if (field != null && _confirmationService.RequiresConfirmation(field, ConfirmationOperation.Delete))
            {
                // Create confirmation model and redirect to confirmation page
                var confirmationModel = _confirmationService.CreateConfirmationModel(
                    field, 
                    ConfirmationOperation.Delete, 
                    itemData, 
                    TaskId, 
                    ReferenceNumber);

                // Store the item data and flow ID in session for after confirmation
                HttpContext.Session.SetString($"PendingDelete_{fieldId}_{itemId}", JsonSerializer.Serialize(new
                {
                    ItemData = itemData,
                    FlowId = flowId,
                    FlowTitle = flowTitle
                }));

                var confirmationUrl = _formNavigationService.GetConfirmationUrl(
                    TaskId, 
                    ReferenceNumber, 
                    "delete", 
                    fieldId, 
                    confirmationModel.ConfirmationToken);

                _logger.LogInformation("Redirecting to delete confirmation page - URL: {ConfirmationUrl}, Field: {FieldId}, Operation: delete", 
                    confirmationUrl, fieldId);

                return Redirect(confirmationUrl);
            }

            // No confirmation required, proceed with deletion
            return await PerformCollectionItemRemoval(fieldId, itemId, flowId, itemData, flowTitle);
        }

        /// <summary>
        /// Handles confirmed delete operations after user confirmation
        /// </summary>
        public async Task<IActionResult> OnPostConfirmedDeleteAsync(string fieldId, string itemId, string? flowId = null)
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

            // Retrieve the pending delete data from session
            var pendingDeleteKey = $"PendingDelete_{fieldId}_{itemId}";
            var pendingDeleteJson = HttpContext.Session.GetString(pendingDeleteKey);
            
            Dictionary<string, object>? itemData = null;
            string? flowTitle = null;
            
            if (!string.IsNullOrEmpty(pendingDeleteJson))
            {
                try
                {
                    var pendingData = JsonSerializer.Deserialize<Dictionary<string, object>>(pendingDeleteJson);
                    if (pendingData != null)
                    {
                        if (pendingData.TryGetValue("ItemData", out var itemDataObj) && itemDataObj != null)
                        {
                            itemData = JsonSerializer.Deserialize<Dictionary<string, object>>(itemDataObj.ToString() ?? "{}");
                        }
                        if (pendingData.TryGetValue("FlowTitle", out var flowTitleObj))
                        {
                            flowTitle = flowTitleObj?.ToString();
                        }
                        if (pendingData.TryGetValue("FlowId", out var flowIdObj))
                        {
                            flowId = flowIdObj?.ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize pending delete data for field {FieldId}, item {ItemId}", fieldId, itemId);
                }
                
                // Clear the pending delete data
                HttpContext.Session.Remove(pendingDeleteKey);
            }

            // Perform the actual deletion
            return await PerformCollectionItemRemoval(fieldId, itemId, flowId, itemData, flowTitle);
        }

        /// <summary>
        /// Handles confirmed add operations after user confirmation
        /// </summary>
        public async Task<IActionResult> OnPostConfirmedAddAsync(string fieldId)
        {
            await CommonFormEngineInitializationAsync();
            
            if (!string.IsNullOrEmpty(TaskId))
            {
                var (group, task) = InitializeCurrentTask(TaskId);
                CurrentGroup = group;
                CurrentTask = task;
            }
            
            if (string.IsNullOrEmpty(fieldId))
            {
                return BadRequest("Field ID is required");
            }

            // For now, just redirect back to the task summary
            // In a real implementation, you would perform the add operation here
            return Redirect(_formNavigationService.GetTaskSummaryUrl(TaskId, ReferenceNumber));
        }

        /// <summary>
        /// Handles confirmed update operations after user confirmation
        /// </summary>
        public async Task<IActionResult> OnPostConfirmedUpdateAsync(string fieldId)
        {
            _logger.LogInformation("OnPostConfirmedUpdateAsync: Starting for fieldId: {FieldId}", fieldId);
            
            try
            {
                await CommonFormEngineInitializationAsync();
                
                // Retrieve the pending form submission data from session first to get the original page ID
                var pendingFormSubmissionJson = HttpContext.Session.GetString("PendingFormSubmission");
                string? originalPageId = null;
                Dictionary<string, object>? formData = null;
                
                if (!string.IsNullOrEmpty(pendingFormSubmissionJson))
                {
                    try
                    {
                        var pendingData = JsonSerializer.Deserialize<Dictionary<string, object>>(pendingFormSubmissionJson);
                        if (pendingData != null && pendingData.TryGetValue("FormData", out var formDataObj))
                        {
                            formData = JsonSerializer.Deserialize<Dictionary<string, object>>(formDataObj.ToString() ?? "{}");
                            
                            // Get the original page ID from the stored data
                            if (pendingData.TryGetValue("CurrentPageId", out var pageIdObj))
                            {
                                originalPageId = pageIdObj?.ToString();
                                _logger.LogDebug("Retrieved original page ID from session: {OriginalPageId}", originalPageId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to deserialize pending form submission data for field {FieldId}", fieldId);
                    }
                }
                
                if (!string.IsNullOrEmpty(TaskId))
                {
                    var (group, task) = InitializeCurrentTask(TaskId);
                    CurrentGroup = group;
                    CurrentTask = task;
                    
                    // Use the original page ID from session to set CurrentPage for navigation after save
                    if (!string.IsNullOrEmpty(originalPageId))
                    {
                        var (_, _, page) = InitializeCurrentPage(originalPageId);
                        CurrentPage = page;
                        _logger.LogDebug("Set CurrentPage to {PageId} for navigation using original page ID from session", CurrentPage?.PageId);
                    }
                    else
                    {
                        _logger.LogWarning("No original page ID found in session, CurrentPage will remain null");
                    }
                }
                
                if (string.IsNullOrEmpty(fieldId))
                {
                    _logger.LogError("FieldId is required but not provided");
                    return BadRequest("Field ID is required");
                }

                // Check if the user confirmed the selection
                var userConfirmed = Request.Form["UserConfirmed"].ToString();
                _logger.LogInformation("OnPostConfirmedUpdateAsync: User confirmed value: {UserConfirmed}", userConfirmed);

                if (string.IsNullOrEmpty(userConfirmed))
                {
                    _logger.LogError("UserConfirmed value is required but not provided");
                    return BadRequest("Please select Yes or No");
                }

                // If user selected "No", don't save the data and redirect back to the previous page
                if (userConfirmed == "false")
                {
                    _logger.LogInformation("User selected 'No', not saving data and redirecting back to previous page");
                    
                    // Clear the pending form submission data
                    HttpContext.Session.Remove("PendingFormSubmission");
                    
                    // Redirect back to the previous page where the user was
                    if (!string.IsNullOrEmpty(originalPageId))
                    {
                        var previousPageUrl = _formNavigationService.GetPageUrl(TaskId, ReferenceNumber, originalPageId);
                        _logger.LogInformation("Redirecting back to previous page: {PreviousPageUrl}", previousPageUrl);
                        return Redirect(previousPageUrl);
                    }
                    else
                    {
                        // Fallback to task summary if we can't determine the previous page
                        var taskSummaryUrl = _formNavigationService.GetTaskSummaryUrl(TaskId, ReferenceNumber);
                        _logger.LogInformation("Fallback redirect to task summary: {TaskSummaryUrl}", taskSummaryUrl);
                        return Redirect(taskSummaryUrl);
                    }
                }

                _logger.LogInformation("OnPostConfirmedUpdateAsync: Processing confirmed update for field: {FieldId}", fieldId);

                // Process the confirmed form data (user selected "Yes")
                if (formData != null)
                {
                    _logger.LogInformation("OnPostConfirmedUpdateAsync: Retrieved pending form data with {Count} fields", formData.Count);
                    
                    // Save the confirmed form data to the API
                    if (ApplicationId.HasValue)
                    {
                        await _applicationResponseService.SaveApplicationResponseAsync(ApplicationId.Value, formData, HttpContext.Session);
                        _logger.LogInformation("Successfully saved confirmed response for Application {ApplicationId}, Field {FieldId}",
                            ApplicationId.Value, fieldId);
                    }
                    
                    // Clear the pending form submission data
                    HttpContext.Session.Remove("PendingFormSubmission");
                    
                    // Redirect to the next page or task summary
                    if (CurrentTask != null)
                    {
                        var nextUrl = _formNavigationService.GetNextNavigationTargetAfterSave(CurrentPage, CurrentTask, ReferenceNumber);
                        _logger.LogInformation("Redirecting to next URL: {NextUrl}", nextUrl);
                        return Redirect(nextUrl);
                    }
                }
                else
                {
                    _logger.LogWarning("No pending form submission data found in session for field {FieldId}", fieldId);
                }

                // Fallback: redirect back to the task summary
                var fallbackUrl = _formNavigationService.GetTaskSummaryUrl(TaskId, ReferenceNumber);
                _logger.LogInformation("Fallback redirect to task summary: {FallbackUrl}", fallbackUrl);
                return Redirect(fallbackUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnPostConfirmedUpdateAsync for fieldId: {FieldId}", fieldId);
                
                // Fallback: redirect back to the task summary
                var fallbackUrl = _formNavigationService.GetTaskSummaryUrl(TaskId, ReferenceNumber);
                _logger.LogInformation("Error fallback redirect to task summary: {FallbackUrl}", fallbackUrl);
                return Redirect(fallbackUrl);
            }
        }

        /// <summary>
        /// Performs the actual removal of a collection item
        /// </summary>
        private async Task<IActionResult> PerformCollectionItemRemoval(string fieldId, string itemId, string? flowId, Dictionary<string, object>? itemData, string? flowTitle)
        {
            // Get current collection from session
            var accumulatedData = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
                    
                    // Generate success message using custom message or fallback
            if (!string.IsNullOrEmpty(flowId) && CurrentTask != null)
            {
                var flow = CurrentTask.Summary?.Flows?.FirstOrDefault(f => f.FlowId == flowId);
                if (flow != null)
                {
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

        /// <summary>
        /// Finds a field by ID in the current template
        /// </summary>
        private Field? FindFieldById(string fieldId)
        {
            if (Template?.TaskGroups == null) return null;

            foreach (var group in Template.TaskGroups)
            {
                if (group.Tasks == null) continue;

                foreach (var task in group.Tasks)
                {
                    if (task.Pages == null) continue;

                    foreach (var page in task.Pages)
                    {
                        if (page.Fields == null) continue;

                        var field = page.Fields.FirstOrDefault(f => f.FieldId == fieldId);
                        if (field != null) return field;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets fields that require confirmation from the current page only
        /// </summary>
        /// <param name="formData">The form data to check</param>
        /// <returns>List of fields that require confirmation</returns>
        private List<Field> GetFieldsRequiringConfirmation(Dictionary<string, object> formData)
        {
            var fieldsRequiringConfirmation = new List<Field>();
            
            // Only check fields on the current page, not the entire template
            if (CurrentPage?.Fields == null) 
            {
                _logger.LogDebug("CurrentPage or Fields is null, cannot check for confirmation requirements");
                return fieldsRequiringConfirmation;
            }

            _logger.LogDebug("Checking {FieldCount} fields on current page for confirmation requirements", CurrentPage.Fields.Count);

            foreach (var field in CurrentPage.Fields)
            {
                // Check if the field has data - form data keys are prefixed with "Data_"
                var dataKey = "Data_" + field.FieldId;
                if (formData.ContainsKey(dataKey) && 
                    !string.IsNullOrWhiteSpace(formData[dataKey]?.ToString()))
                {
                    _logger.LogDebug("Field {FieldId} has data at key {DataKey}, checking if it requires confirmation", field.FieldId, dataKey);
                    
                    // Now call the confirmation service to check if this field requires confirmation
                    if (_confirmationService.RequiresConfirmation(field, ConfirmationOperation.Update))
                    {
                        _logger.LogDebug("Field {FieldId} requires confirmation and will be added to list", field.FieldId);
                        fieldsRequiringConfirmation.Add(field);
                    }
                    else
                    {
                        _logger.LogDebug("Field {FieldId} does NOT require confirmation", field.FieldId);
                    }
                }
                else
                {
                    _logger.LogDebug("Field {FieldId} has no data or empty data at key {DataKey}, skipping confirmation check", field.FieldId, dataKey);
                }
            }

            _logger.LogDebug("GetFieldsRequiringConfirmation returning {Count} fields that require confirmation", 
                fieldsRequiringConfirmation.Count);
            
            return fieldsRequiringConfirmation;
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
        /// Parses confirmation route segments to extract operation, fieldId, and confirmationToken
        /// </summary>
        /// <param name="pageId">The page ID to parse</param>
        /// <param name="operation">The operation being confirmed</param>
        /// <param name="fieldId">The field ID</param>
        /// <param name="confirmationToken">The confirmation token</param>
        /// <returns>True if the route is a confirmation route</returns>
        private bool TryParseConfirmationRoute(string pageId, out string operation, out string fieldId, out string confirmationToken)
        {
            operation = fieldId = confirmationToken = string.Empty;
            if (string.IsNullOrEmpty(pageId)) return false;
            
            // Expected: confirm/{operation}/{fieldId}/{confirmationToken}
            var parts = pageId.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            // Log the parsing attempt for debugging
            _logger.LogDebug("TryParseConfirmationRoute: pageId='{PageId}', parts.Length={PartsLength}, parts=[{Parts}]", 
                pageId, parts.Length, string.Join(", ", parts));
            
            if (parts.Length >= 4 && parts[0].Equals("confirm", StringComparison.OrdinalIgnoreCase))
            {
                operation = parts[1];
                fieldId = parts[2];
                confirmationToken = parts[3];
                
                _logger.LogDebug("TryParseConfirmationRoute: SUCCESS - operation='{Operation}', fieldId='{FieldId}', token='{Token}'", 
                    operation, fieldId, confirmationToken);
                return true;
            }
            
            _logger.LogDebug("TryParseConfirmationRoute: FAILED - parts[0]='{FirstPart}', expected 'confirm'", parts[0]);
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


    }
}







