using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Pages.Shared;
using DfE.ExternalApplications.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Task = System.Threading.Tasks.Task;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.IO;

namespace DfE.ExternalApplications.Web.Pages.FormEngine
{
    [ExcludeFromCodeCoverage]
    public class RenderFormModel(
        IFieldRendererService renderer,
        IApplicationResponseService applicationResponseService,
        IFieldFormattingService fieldFormattingService,
        ITemplateManagementService templateManagementService,
        IApplicationStateService applicationStateService,
        IAutocompleteService autocompleteService,
        IFileUploadService fileUploadService,
        ILogger<RenderFormModel> logger)
        : BaseFormPageModel(renderer, applicationResponseService, fieldFormattingService, templateManagementService,
            applicationStateService, logger)
    {
        [BindProperty] public Dictionary<string, object> Data { get; set; } = new();
        [BindProperty] public string CurrentPageId { get; set; }

        public TaskGroup CurrentGroup { get; set; }
        public Domain.Models.Task CurrentTask { get; set; }
        public Domain.Models.Page CurrentPage { get; set; }

        public async Task OnGetAsync(string pageId)
        {
            await CommonInitializationAsync();
            CurrentPageId = pageId;

            // If application is not editable and trying to access a specific page, redirect to preview
            if (!IsApplicationEditable() && !string.IsNullOrEmpty(pageId))
            {
                Response.Redirect($"~/render-form/{ReferenceNumber}/preview");
                return;
            }

            if (!string.IsNullOrEmpty(pageId))
            {
                var (group, task, page) = InitializeCurrentPage(CurrentPageId);
                CurrentGroup = group;
                CurrentTask = task;
                CurrentPage = page;
            }

            // Check if we need to clear session data for a new application
            CheckAndClearSessionForNewApplication();

            // Load accumulated form data from session to pre-populate fields
            LoadAccumulatedDataFromSession();
        }

        public async Task<IActionResult> OnPostPageAsync()
        {
            await CommonInitializationAsync();

            // Prevent editing if application is not editable
            if (!IsApplicationEditable())
            {
                return RedirectToPage("/ApplicationPreview", new { referenceNumber = ReferenceNumber });
            }

            var (group, task, page) = InitializeCurrentPage(CurrentPageId);
            CurrentGroup = group;
            CurrentTask = task;
            CurrentPage = page;

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

            // Redirect to the task summary page after saving
            return RedirectToPage("/Applications/TaskSummary", new { referenceNumber = ReferenceNumber, taskId = CurrentTask.TaskId });
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

        // --- Upload field handlers ---
        public async Task<IActionResult> OnPostUploadFileAsync(string applicationId)
        {
            await CommonInitializationAsync();
            if (!Guid.TryParse(applicationId, out var appId))
            {
                ViewData["UploadError"] = "Invalid application ID.";
                return Page();
            }
            var file = Request.Form.Files["UploadFile"];
            var name = Request.Form["UploadName"].ToString();
            var description = Request.Form["UploadDescription"].ToString();
            var fieldId = GetUploadFieldIdFromRequest();
            if (file == null || file.Length == 0)
            {
                ViewData[$"{fieldId}_UploadError"] = "Please select a file to upload.";
                await PopulateUploadFilesForField(appId, fieldId);
                return Page();
            }
            try
            {
                using var stream = file.OpenReadStream();
                var fileParam = new FileParameter(stream, file.FileName, file.ContentType);
                await fileUploadService.UploadFileAsync(appId, name, description, fileParam);
                ViewData[$"{fieldId}_UploadSuccess"] = $"Your file '{file.FileName}' uploaded.";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error uploading file for application {ApplicationId}", appId);
                ViewData[$"{fieldId}_UploadError"] = "There was a problem uploading your file.";
            }
            await PopulateUploadFilesForField(appId, fieldId);
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteFileAsync(string applicationId)
        {
            await CommonInitializationAsync();
            if (!Guid.TryParse(applicationId, out var appId))
            {
                ViewData["UploadError"] = "Invalid application ID.";
                return Page();
            }
            var fileIdStr = Request.Form["FileId"].ToString();
            var fieldId = GetUploadFieldIdFromRequest();
            if (!Guid.TryParse(fileIdStr, out var fileId))
            {
                ViewData[$"{fieldId}_UploadError"] = "Invalid file ID.";
                await PopulateUploadFilesForField(appId, fieldId);
                return Page();
            }
            try
            {
                await fileUploadService.DeleteFileAsync(fileId, appId);
                ViewData[$"{fieldId}_UploadSuccess"] = "File deleted.";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting file {FileId} for application {ApplicationId}", fileId, appId);
                ViewData[$"{fieldId}_UploadError"] = "There was a problem deleting the file.";
            }
            await PopulateUploadFilesForField(appId, fieldId);
            return Page();
        }

        ////public async Task<IActionResult> OnPostDownloadFileAsync(string applicationId)
        ////{
        ////    await CommonInitializationAsync();
        ////    if (!Guid.TryParse(applicationId, out var appId))
        ////    {
        ////        ViewData["UploadError"] = "Invalid application ID.";
        ////        return Page();
        ////    }
        ////    var fileIdStr = Request.Form["FileId"].ToString();
        ////    if (!Guid.TryParse(fileIdStr, out var fileId))
        ////    {
        ////        ViewData["UploadError"] = "Invalid file ID.";
        ////        return Page();
        ////    }
        ////    try
        ////    {
        ////        var fileParam = await fileUploadService.DownloadFileAsync(fileId, appId);
        ////        return File(fileParam.Stream);
        ////    }
        ////    catch (Exception ex)
        ////    {
        ////        logger.LogError(ex, "Error downloading file {FileId} for application {ApplicationId}", fileId, appId);
        ////        ViewData["UploadError"] = "There was a problem downloading the file.";
        ////        return Page();
        ////    }
        ////}

        private async Task PopulateUploadFilesForField(Guid applicationId, string fieldId)
        {
            try
            {
                var files = await fileUploadService.GetFilesForApplicationAsync(applicationId);
                ViewData[$"{fieldId}_Files"] = files;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting files for application {ApplicationId}", applicationId);
                ViewData[$"{fieldId}_Files"] = new List<UploadDto>();
            }
        }

        private string GetUploadFieldIdFromRequest()
        {
            // For now, assume only one upload field per page; otherwise, pass fieldId as hidden input
            // Could parse from form or use a convention
            return CurrentPage?.Fields.FirstOrDefault(f => f.Type == "upload")?.FieldId ?? "Upload";
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




    }
}







