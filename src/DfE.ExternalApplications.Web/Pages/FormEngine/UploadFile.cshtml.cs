using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using DfE.ExternalApplications.Application.Interfaces;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DfE.ExternalApplications.Web.Pages.FormEngine
{
    public class UploadFileModel : PageModel
    {
        private readonly IFileUploadService _fileUploadService;
        public UploadFileModel(IFileUploadService fileUploadService)
        {
            _fileUploadService = fileUploadService;
        }

        [BindProperty(SupportsGet = true)]
        public string ApplicationId { get; set; }
        [BindProperty(SupportsGet = true)]
        public string FieldId { get; set; }
        public IReadOnlyList<UploadDto> Files { get; set; } = new List<UploadDto>();
        public string SuccessMessage { get; set; }
        public string ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!Guid.TryParse(ApplicationId, out var appId))
                return NotFound();
            Files = await _fileUploadService.GetFilesForApplicationAsync(appId);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!Guid.TryParse(ApplicationId, out var appId))
                return NotFound();
            var file = Request.Form.Files["UploadFile"];
            var name = Request.Form["UploadName"].ToString();
            var description = Request.Form["UploadDescription"].ToString();
            if (file == null || file.Length == 0)
            {
                ErrorMessage = "Please select a file to upload.";
                Files = await _fileUploadService.GetFilesForApplicationAsync(appId);
                return Page();
            }
            try
            {
                using var stream = file.OpenReadStream();
                var fileParam = new FileParameter(stream, file.FileName, file.ContentType);
                await _fileUploadService.UploadFileAsync(appId, name, description, fileParam);
                SuccessMessage = $"Your file '{file.FileName}' uploaded.";
            }
            catch
            {
                ErrorMessage = "There was a problem uploading your file.";
            }
            Files = await _fileUploadService.GetFilesForApplicationAsync(appId);
            UpdateSessionFileList(appId, FieldId, Files);
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteFileAsync()
        {
            if (!Guid.TryParse(ApplicationId, out var appId))
                return NotFound();
            var fileIdStr = Request.Form["FileId"].ToString();
            if (!Guid.TryParse(fileIdStr, out var fileId))
            {
                ErrorMessage = "Invalid file ID.";
                Files = await _fileUploadService.GetFilesForApplicationAsync(appId);
                return Page();
            }
            try
            {
                await _fileUploadService.DeleteFileAsync(fileId, appId);
                SuccessMessage = "File deleted.";
            }
            catch
            {
                ErrorMessage = "There was a problem deleting the file.";
            }
            Files = await _fileUploadService.GetFilesForApplicationAsync(appId);
            UpdateSessionFileList(appId, FieldId, Files);
            return Page();
        }

        //public async Task<IActionResult> OnPostDownloadFileAsync()
        //{
        //    if (!Guid.TryParse(ApplicationId, out var appId))
        //        return NotFound();
        //    var fileIdStr = Request.Form["FileId"].ToString();
        //    if (!Guid.TryParse(fileIdStr, out var fileId))
        //        return NotFound();
        //    var fileResponse = await _fileUploadService.DownloadFileAsync(fileId, appId);
        //    return File(fileResponse.Data, fileResponse.ContentType, fileResponse.FileName);
        //}

        private void UpdateSessionFileList(Guid appId, string fieldId, IReadOnlyList<UploadDto> files)
        {
            var key = $"UploadedFiles_{appId}_{fieldId}";
            HttpContext.Session.SetString(key, System.Text.Json.JsonSerializer.Serialize(files));
        }
    }
} 