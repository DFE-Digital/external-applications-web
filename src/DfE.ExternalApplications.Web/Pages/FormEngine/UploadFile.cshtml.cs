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
        [BindProperty] public string FlowId { get; set; } = string.Empty;
        [BindProperty] public string InstanceId { get; set; } = string.Empty;
        public IReadOnlyList<UploadDto> Files { get; set; } = new List<UploadDto>();
        public string SuccessMessage { get; set; }
        public string ErrorMessage { get; set; }
        
        private bool IsCollectionFlow => !string.IsNullOrEmpty(FlowId) && !string.IsNullOrEmpty(InstanceId);

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
            // Debug: Check for validation errors
            Console.WriteLine($"[UPLOAD DEBUG] IsCollectionFlow: {IsCollectionFlow}");
            Console.WriteLine($"[UPLOAD DEBUG] FlowId: '{FlowId}', InstanceId: '{InstanceId}'");
            
            // Clear validation errors for FlowId and InstanceId when not in collection flow
            if (!IsCollectionFlow)
            {
                ModelState.Remove("FlowId");
                ModelState.Remove("InstanceId");
            }
            
            Console.WriteLine($"[UPLOAD DEBUG] ModelState.IsValid: {ModelState.IsValid}");
            if (!ModelState.IsValid)
            {
                foreach (var error in ModelState)
                {
                    foreach (var errorMsg in error.Value.Errors)
                    {
                        Console.WriteLine($"[UPLOAD DEBUG] ModelState Error - {error.Key}: {errorMsg.ErrorMessage}");
                    }
                }
            }
            
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
            Console.WriteLine($"[UPLOAD DEBUG] Current files count before adding new file: {currentFieldFiles.Count}");
            foreach (var existingFile in currentFieldFiles)
            {
                Console.WriteLine($"[UPLOAD DEBUG] Existing file: {existingFile.OriginalFileName} (ID: {existingFile.Id})");
            }
            
            // Get the latest file list from database to find the newly uploaded file
            var allDbFiles = await fileUploadService.GetFilesForApplicationAsync(appId);
            var newlyUploadedFile = allDbFiles
                .Where(f => !currentFieldFiles.Any(cf => cf.Id == f.Id))
                .OrderByDescending(f => f.UploadedOn)
                .FirstOrDefault();
            
            // Add the newly uploaded file to our field's file list
            if (newlyUploadedFile != null)
            {
                Console.WriteLine($"[UPLOAD DEBUG] Adding new file: {newlyUploadedFile.OriginalFileName} (ID: {newlyUploadedFile.Id})");
                currentFieldFiles.Add(newlyUploadedFile);
            }
            
            Console.WriteLine($"[UPLOAD DEBUG] Total files count after adding new file: {currentFieldFiles.Count}");
            
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
                
                // CRITICAL FIX: Try multiple sources to find existing flow data
                var existingProgress = LoadFlowProgress();
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

            string? sessionFilesJson = null;

            if (IsCollectionFlow)
            {
                // For collection flows, get files from flow progress system
                Console.WriteLine($"[UPLOAD DEBUG] Loading files for collection flow - FlowId: {FlowId}, InstanceId: {InstanceId}");
                var progressData = LoadFlowProgress();
                Console.WriteLine($"[UPLOAD DEBUG] Progress data keys: {string.Join(", ", progressData.Keys)}");
                
                if (progressData.TryGetValue(fieldId, out var progressValue))
                {
                    sessionFilesJson = progressValue?.ToString();
                    Console.WriteLine($"[UPLOAD DEBUG] Found files in flow progress: {sessionFilesJson?.Substring(0, Math.Min(200, sessionFilesJson?.Length ?? 0))}...");
                }
                else
                {
                    // CRITICAL FIX: Flow progress not found, initialize from database if possible
                    Console.WriteLine($"[UPLOAD DEBUG] Flow progress empty, initializing from database and session sources");
                    
                    // 1. Check if any files exist in database for this application
                    // Note: Since UploadDto doesn't have FieldId, we'll rely on session data for field association
                    try
                    {
                        var allDbFiles = await fileUploadService.GetFilesForApplicationAsync(appId);
                        Console.WriteLine($"[UPLOAD DEBUG] Found {allDbFiles.Count()} total files in database for application");
                        
                        // For now, we can't filter by field ID since UploadDto doesn't have that property
                        // We'll rely on session data to maintain field-specific file associations
                        Console.WriteLine($"[UPLOAD DEBUG] Cannot initialize from database - UploadDto lacks FieldId property");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[UPLOAD DEBUG] Error loading files from database: {ex.Message}");
                    }
                    
                    // 2. If still no files, check accumulated form data
                    if (string.IsNullOrWhiteSpace(sessionFilesJson))
                    {
                        var alternativeAccumulatedData = applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
                        if (alternativeAccumulatedData.TryGetValue(fieldId, out var accFieldValue))
                        {
                            sessionFilesJson = accFieldValue?.ToString();
                            Console.WriteLine($"[UPLOAD DEBUG] Found files in accumulated data: {sessionFilesJson?.Substring(0, Math.Min(200, sessionFilesJson?.Length ?? 0))}...");
                        }
                    }
                    
                    // 3. If still not found, search all session keys for this field data
                    if (string.IsNullOrWhiteSpace(sessionFilesJson))
                    {
                        Console.WriteLine($"[UPLOAD DEBUG] Searching all session keys for field data");
                        foreach (var sessionKey in HttpContext.Session.Keys)
                        {
                            var keyValue = HttpContext.Session.GetString(sessionKey);
                            if (!string.IsNullOrWhiteSpace(keyValue))
                            {
                                // Check if this key contains our field data
                                if (sessionKey.Contains(fieldId, StringComparison.OrdinalIgnoreCase) ||
                                    (keyValue.StartsWith("[") && keyValue.Contains("\"id\"") && keyValue.Contains(fieldId)))
                                {
                                    Console.WriteLine($"[UPLOAD DEBUG] Found potential field data in session key '{sessionKey}': {keyValue.Substring(0, Math.Min(100, keyValue.Length))}...");
                                    sessionFilesJson = keyValue;
                                    break;
                                }
                                
                                // Also check if the key contains flow progress for our specific flow
                                if (sessionKey.Contains($"FlowProgress_{FlowId}") && keyValue.Contains(fieldId))
                                {
                                    Console.WriteLine($"[UPLOAD DEBUG] Found flow progress data in key '{sessionKey}', extracting field data");
                                    try
                                    {
                                        var flowData = JsonSerializer.Deserialize<Dictionary<string, object>>(keyValue);
                                        if (flowData != null && flowData.TryGetValue(fieldId, out var fieldData))
                                        {
                                            sessionFilesJson = fieldData?.ToString();
                                            Console.WriteLine($"[UPLOAD DEBUG] Extracted field data from flow progress: {sessionFilesJson?.Substring(0, Math.Min(100, sessionFilesJson?.Length ?? 0))}...");
                                            break;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[UPLOAD DEBUG] Error parsing flow progress: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }
                
                Console.WriteLine($"[UPLOAD DEBUG] Session files JSON from flow progress: {sessionFilesJson?.Substring(0, Math.Min(200, sessionFilesJson?.Length ?? 0))}...");
            }
            else
            {
                // For regular forms, get files from session
                var sessionKey = $"UploadedFiles_{appId}_{fieldId}";
                sessionFilesJson = HttpContext.Session.GetString(sessionKey);
            }
            
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

        /// <summary>
        /// Helper methods for collection flow support
        /// </summary>
        private static string GetFlowProgressSessionKey(string flowId, string instanceId) => $"FlowProgress_{flowId}_{instanceId}";

        private Dictionary<string, object> LoadFlowProgress()
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
            Console.WriteLine($"[UPLOAD DEBUG] Flow progress JSON from session: {json?.Substring(0, Math.Min(100, json?.Length ?? 0))}...");
            
            if (string.IsNullOrWhiteSpace(json))
            {
                Console.WriteLine($"[UPLOAD DEBUG] No flow progress data found in session");
                return new Dictionary<string, object>();
            }

            try
            {
                var result = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
                Console.WriteLine($"[UPLOAD DEBUG] Successfully loaded flow progress with {result.Count} keys: {string.Join(", ", result.Keys)}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UPLOAD DEBUG] Failed to deserialize flow progress: {ex.Message}");
                return new Dictionary<string, object>();
            }
        }
    }
}