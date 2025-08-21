using DfE.CoreLibs.Contracts.ExternalApplications.Enums;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Request;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using DfE.ExternalApplications.Application.Interfaces;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using DfE.ExternalApplications.Web.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace DfE.ExternalApplications.Web.Pages.FormEngine
{
    public class UploadFileModel(
        IFileUploadService fileUploadService,
        IApplicationResponseService applicationResponseService,
        INotificationsClient notificationsClient,
        IFormErrorStore formErrorStore)
        : PageModel
    {
        [BindProperty(SupportsGet = true)] public string ApplicationId { get; set; }
        [BindProperty(SupportsGet = true)] public string FieldId { get; set; }
        [BindProperty(SupportsGet = true, Name = "referenceNumber")] public string ReferenceNumber { get; set; }
        [BindProperty(SupportsGet = true, Name = "taskId")] public string TaskId { get; set; }
        [BindProperty(SupportsGet = true, Name = "pageId")] public string CurrentPageId { get; set; }
        [BindProperty] public string ReturnUrl { get; set; }
        public IReadOnlyList<UploadDto> Files { get; set; } = new List<UploadDto>();
        public string SuccessMessage { get; set; }
        public string ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!Guid.TryParse(ApplicationId, out var appId))
                return NotFound();
            
            // Get only files for this specific field ID
            Files = await GetFilesForFieldAsync(appId, FieldId);
            return Page();
        }

        public async Task<IActionResult> OnPostUploadFileAsync()
        {
            var addRequest = new AddNotificationRequest
            {
                Message = string.Empty, // set later when known
                Category = "file-upload",
                Context = FieldId + "FileUpload",
                Type = NotificationType.Success,
                AutoDismiss = false,
                AutoDismissSeconds = 5
            };

            if (!Guid.TryParse(ApplicationId, out var appId))
                return NotFound();
            var file = Request.Form.Files["UploadFile"];
            var name = Request.Form["UploadName"].ToString();
            var description = Request.Form["UploadDescription"].ToString();
            if (file == null || file.Length == 0)
            {
                ErrorMessage = "Please select a file to upload.";
                ModelState.AddModelError("UploadFile", ErrorMessage);
                if (!string.IsNullOrEmpty(FieldId))
                {
                    // Persist field-level errors only to avoid duplicate summary lines
                    formErrorStore.Save(FieldId, ModelState);
                }

                // If we have a return URL, redirect back with error
                if (!string.IsNullOrEmpty(ReturnUrl))
                {
                    return Redirect(ReturnUrl);
                }
                
                Files = await GetFilesForFieldAsync(appId, FieldId);
                return Page();
            }

            using var stream = file.OpenReadStream();
            var fileParam = new FileParameter(stream, file.FileName, file.ContentType);
            await fileUploadService.UploadFileAsync(appId, file.FileName, description, fileParam);
            SuccessMessage = $"Your file '{file.FileName}' uploaded.";

            // Get the current files for this field
            var currentFieldFiles = (await GetFilesForFieldAsync(appId, FieldId)).ToList();
            
            // Get the latest file list from database to find the newly uploaded file
            var allDbFiles = await fileUploadService.GetFilesForApplicationAsync(appId);
            var newlyUploadedFile = allDbFiles
                .Where(f => !currentFieldFiles.Any(cf => cf.Id == f.Id))
                .OrderByDescending(f => f.UploadedOn)
                .FirstOrDefault();
            
            // Add the newly uploaded file to our field's file list
            if (newlyUploadedFile != null)
            {
                currentFieldFiles.Add(newlyUploadedFile);
            }
            
            Files = currentFieldFiles.AsReadOnly();
            UpdateSessionFileList(appId, FieldId, Files);
            await SaveUploadedFilesToResponseAsync(appId, FieldId, Files);
            
            // If we have a return URL (from partial form), redirect back
            if (!string.IsNullOrEmpty(ReturnUrl))
            {
                addRequest.Message = SuccessMessage;
                await notificationsClient.CreateNotificationAsync(addRequest);
                return Redirect(ReturnUrl);
            }

            return Page();
        }

        public override void OnPageHandlerExecuted(PageHandlerExecutedContext context)
        {
            base.OnPageHandlerExecuted(context);
            
            // If there are ModelState errors (from the filter), persist them via the error store
            if (!ModelState.IsValid && !string.IsNullOrEmpty(FieldId))
            {
                formErrorStore.Save(FieldId, ModelState);
                
                // If we have a return URL, redirect back with errors
                if (!string.IsNullOrEmpty(ReturnUrl))
                {
                    context.Result = new RedirectResult(ReturnUrl);
                }
            }
        }

        public async Task<IActionResult> OnPostDeleteFileAsync()
        {
            var addRequest = new AddNotificationRequest
            {
                Message = string.Empty,
                Category = "file-upload",
                Context = FieldId + "FileDeletion",
                Type = NotificationType.Success,
                AutoDismiss = false,
                AutoDismissSeconds = 5
            };

            if (!Guid.TryParse(ApplicationId, out var appId))
                return NotFound();
            var fileIdStr = Request.Form["FileId"].ToString();
            if (!Guid.TryParse(fileIdStr, out var fileId))
            {
                ErrorMessage = "Invalid file ID.";
                if (!string.IsNullOrEmpty(FieldId))
                {
                    formErrorStore.Save(FieldId, ModelState, ErrorMessage);
                }
                
                // If we have a return URL, redirect back with error
                if (!string.IsNullOrEmpty(ReturnUrl))
                {
                    return Redirect(ReturnUrl);
                }
                
                Files = await GetFilesForFieldAsync(appId, FieldId);
                return Page();
            }

            await fileUploadService.DeleteFileAsync(fileId, appId);
            SuccessMessage = "File deleted.";

            // Get current files for this field and remove the deleted one
            var currentFieldFiles = (await GetFilesForFieldAsync(appId, FieldId)).ToList();
            currentFieldFiles.RemoveAll(f => f.Id == fileId);
            
            Files = currentFieldFiles.AsReadOnly();
            UpdateSessionFileList(appId, FieldId, Files);
            await SaveUploadedFilesToResponseAsync(appId, FieldId, Files);
            
            // If we have a return URL (from partial form), redirect back
            if (!string.IsNullOrEmpty(ReturnUrl))
            {
                addRequest.Message = SuccessMessage;
                await notificationsClient.CreateNotificationAsync(addRequest);
                return Redirect(ReturnUrl);
            }

            return Page();
        }

        public async Task<IActionResult> OnPostDownloadFileAsync()
        {
            if (!Guid.TryParse(ApplicationId, out var appId))
                return NotFound();
            var fileIdStr = Request.Form["FileId"].ToString();
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


        private void UpdateSessionFileList(Guid appId, string fieldId, IReadOnlyList<UploadDto> files)
        {
            var key = $"UploadedFiles_{appId}_{fieldId}";
            HttpContext.Session.SetString(key, System.Text.Json.JsonSerializer.Serialize(files));
        }

        private async Task SaveUploadedFilesToResponseAsync(Guid appId, string fieldId, IReadOnlyList<UploadDto> files)
        {

            if (string.IsNullOrEmpty(fieldId))
            {
                return;
            }

            var json = JsonSerializer.Serialize(files);
            var data = new Dictionary<string, object> { { fieldId, json } };

            await applicationResponseService.SaveApplicationResponseAsync(appId, data, HttpContext.Session);

        }

        /// <summary>
        /// Gets files for a specific field ID by filtering from existing session data first,
        /// then cross-referencing with database files to ensure we only get files for this field
        /// </summary>
        private async Task<IReadOnlyList<UploadDto>> GetFilesForFieldAsync(Guid appId, string fieldId)
        {
            if (string.IsNullOrEmpty(fieldId))
            {
                return new List<UploadDto>().AsReadOnly();
            }

            // First, try to get existing files from session for this field
            var sessionKey = $"UploadedFiles_{appId}_{fieldId}";
            var sessionFilesJson = HttpContext.Session.GetString(sessionKey);
            
            if (!string.IsNullOrEmpty(sessionFilesJson))
            {
                try
                {
                    var sessionFiles = JsonSerializer.Deserialize<List<UploadDto>>(sessionFilesJson);
                    if (sessionFiles != null)
                    {
                        // Cross-reference with database to make sure files still exist
                        var allDbFiles = await fileUploadService.GetFilesForApplicationAsync(appId);
                        var validSessionFiles = sessionFiles
                            .Where(sf => allDbFiles.Any(dbf => dbf.Id == sf.Id))
                            .ToList();
                        
                        return validSessionFiles.AsReadOnly();
                    }
                }
                catch (JsonException)
                {
                    // Session data is corrupted, fall through to check accumulated data
                }
            }

            // If no session data, try to get from accumulated form data (for existing applications)
            var accumulatedData = applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
            if (accumulatedData.TryGetValue(fieldId, out var fieldValue))
            {
                var fieldValueStr = fieldValue?.ToString();
                if (!string.IsNullOrEmpty(fieldValueStr))
                {
                    try
                    {
                        var existingFiles = JsonSerializer.Deserialize<List<UploadDto>>(fieldValueStr);
                        if (existingFiles != null)
                        {
                            // Cross-reference with database to make sure files still exist
                            var allDbFiles = await fileUploadService.GetFilesForApplicationAsync(appId);
                            var validFiles = existingFiles
                                .Where(ef => allDbFiles.Any(dbf => dbf.Id == ef.Id))
                                .ToList();
                            
                            return validFiles.AsReadOnly();
                        }
                    }
                    catch (JsonException)
                    {
                        // Data is corrupted, return empty list
                    }
                }
            }

            // If no existing data for this field, return empty list
            // Don't return all database files, as that would include files from other fields
            return new List<UploadDto>().AsReadOnly();
        }

        // legacy method removed in favour of IFormErrorStore
    }
}