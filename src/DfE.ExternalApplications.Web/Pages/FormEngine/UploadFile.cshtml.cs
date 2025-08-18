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
            Files = await fileUploadService.GetFilesForApplicationAsync(appId);
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
                
                Files = await fileUploadService.GetFilesForApplicationAsync(appId);
                return Page();
            }

            using var stream = file.OpenReadStream();
            var fileParam = new FileParameter(stream, file.FileName, file.ContentType);
            await fileUploadService.UploadFileAsync(appId, file.FileName, description, fileParam);
            SuccessMessage = $"Your file '{file.FileName}' uploaded.";

            Files = await fileUploadService.GetFilesForApplicationAsync(appId);
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
                
                Files = await fileUploadService.GetFilesForApplicationAsync(appId);
                return Page();
            }

            await fileUploadService.DeleteFileAsync(fileId, appId);
            SuccessMessage = "File deleted.";

            Files = await fileUploadService.GetFilesForApplicationAsync(appId);
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

        // legacy method removed in favour of IFormErrorStore
    }
}