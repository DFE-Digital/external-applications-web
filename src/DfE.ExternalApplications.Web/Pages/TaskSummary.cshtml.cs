using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Services;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Web.Pages
{
    [ExcludeFromCodeCoverage]
    public class TaskSummaryModel : PageModel
    {
        public FormTemplate Template { get; set; }
        [BindProperty(SupportsGet = true, Name = "referenceNumber")] public string ReferenceNumber { get; set; }
        [BindProperty(SupportsGet = true, Name = "taskId")] public string TaskId { get; set; }
        [BindProperty] public bool IsTaskCompleted { get; set; }
        public string TemplateId { get; set; }
        public Guid? ApplicationId { get; set; }
        public string ApplicationStatus { get; set; } = "InProgress";

        public TaskGroup CurrentGroup { get; set; }
        public Domain.Models.Task CurrentTask { get; set; }
        public Dictionary<string, object> FormData { get; set; } = new();

        private readonly IFieldRendererService _renderer;
        private readonly IFormTemplateProvider _templateProvider;
        private readonly IFormTemplateParser _templateParser;
        private readonly IApplicationResponseService _applicationResponseService;
        private readonly IApplicationsClient _applicationsClient;
        private readonly ILogger<TaskSummaryModel> _logger;

        /// <summary>
        /// Stores the current application data to access template schema for existing applications
        /// </summary>
        private ApplicationDto? _currentApplication;

        public TaskSummaryModel(
            IFieldRendererService renderer,
            IFormTemplateProvider templateProvider,
            IFormTemplateParser templateParser,
            IApplicationResponseService applicationResponseService,
            IApplicationsClient applicationsClient,
            ILogger<TaskSummaryModel> logger)
        {
            _renderer = renderer;
            _templateProvider = templateProvider;
            _templateParser = templateParser;
            _applicationResponseService = applicationResponseService;
            _applicationsClient = applicationsClient;
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            TemplateId = HttpContext.Session.GetString("TemplateId") ?? string.Empty;
            await EnsureApplicationIdAsync();
            await LoadTemplateAsync();
            InitializeCurrentTask(TaskId);
            LoadFormDataFromSession();
            LoadApplicationStatus();
            
            // Check if task is already marked as completed from session
            IsTaskCompleted = GetTaskStatusFromSession(TaskId) == Domain.Models.TaskStatus.Completed;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            TemplateId = HttpContext.Session.GetString("TemplateId") ?? string.Empty;
            await EnsureApplicationIdAsync();
            await LoadTemplateAsync();
            InitializeCurrentTask(TaskId);
            LoadApplicationStatus();

            // Prevent editing if application is not editable
            if (!IsApplicationEditable())
            {
                return RedirectToPage("/ApplicationPreview", new { referenceNumber = ReferenceNumber });
            }

            // Update task status based on checkbox
            var newStatus = IsTaskCompleted ? Domain.Models.TaskStatus.Completed : Domain.Models.TaskStatus.InProgress;
            
            // Update the task status in the template (this will need to be persisted)
            CurrentTask.TaskStatusString = newStatus.ToString();
            
            // Save the task status change
            if (ApplicationId.HasValue)
            {
                try
                {
                    // Save task status to the API (we'll need to implement this)
                    await SaveTaskStatusAsync(ApplicationId.Value, TaskId, newStatus);
                    _logger.LogInformation("Successfully updated task status for Application {ApplicationId}, Task {TaskId} to {Status}",
                        ApplicationId.Value, TaskId, newStatus);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save task status for Application {ApplicationId}, Task {TaskId}",
                        ApplicationId.Value, TaskId);
                }
            }

            return RedirectToPage("/RenderForm", new { referenceNumber = ReferenceNumber });
        }

        private void LoadFormDataFromSession()
        {
            FormData = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
        }

        /// <summary>
        /// Loads the appropriate template schema based on whether this is a new or existing application.
        /// For new applications, loads the latest template schema.
        /// For existing applications, loads the template schema version that was used when the application was created.
        /// </summary>
        private async Task LoadTemplateAsync()
        {
            try
            {
                // If we have an existing application with template schema, use that version
                if (_currentApplication?.TemplateSchema != null)
                {
                    _logger.LogDebug("Using template schema from existing application {ApplicationId} with template version {TemplateVersionId}", 
                        _currentApplication.ApplicationId, _currentApplication.TemplateVersionId);
                    
                    Template = await LoadTemplateFromSchemaAsync(_currentApplication.TemplateSchema.JsonSchema);
                }
                else
                {
                    // For new applications or when template schema is not available, use the latest template
                    _logger.LogDebug("Loading latest template schema for template {TemplateId}", TemplateId);
                    Template = await _templateProvider.GetTemplateAsync(TemplateId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load template {TemplateId}", TemplateId);
                throw;
            }
        }

        /// <summary>
        /// Converts a TemplateSchemaDto to a FormTemplate using the template parser.
        /// This ensures consistent parsing logic regardless of the template source.
        /// </summary>
        private async Task<FormTemplate> LoadTemplateFromSchemaAsync(string templateSchema)
        {
            try
            {
                // Convert to stream for parser
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(templateSchema));
                
                // Use the same parser that's used for API templates to ensure consistency
                return await _templateParser.ParseAsync(stream);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse template schema from application");
                throw new InvalidOperationException("Failed to parse template schema from application", ex);
            }
        }

        private void InitializeCurrentTask(string taskId)
        {
            var allTasks = Template.TaskGroups
                .SelectMany(g => g.Tasks.Select(t => new { Group = g, Task = t }))
                .ToList();

            var taskPair = allTasks.FirstOrDefault(x => x.Task.TaskId == taskId);
            
            if (taskPair == null)
            {
                throw new InvalidOperationException($"Task with ID '{taskId}' not found.");
            }

            CurrentGroup = taskPair.Group;
            CurrentTask = taskPair.Task;
        }

        public string GetFieldValue(string fieldId)
        {
            if (FormData.TryGetValue(fieldId, out var value))
            {
                if (value == null)
                {
                    return string.Empty;
                }

                // If it's already a string, return it
                if (value is string stringValue)
                {
                    return stringValue;
                }

                // If it's an object (like from autocomplete), serialize it to JSON
                try
                {
                    return JsonSerializer.Serialize(value);
                }
                catch
                {
                    return value.ToString() ?? string.Empty;
                }
            }
            return string.Empty;
        }

        public string GetFormattedFieldValue(string fieldId)
        {
            var fieldValue = GetFieldValue(fieldId);
            
            if (string.IsNullOrEmpty(fieldValue))
            {
                return string.Empty;
            }

            // Try to format as autocomplete data if it looks like JSON
            if (fieldValue.StartsWith("{") || fieldValue.StartsWith("["))
            {
                return FormatAutocompleteValue(fieldValue);
            }

            return fieldValue;
        }

        public List<string> GetFormattedFieldValues(string fieldId)
        {
            var fieldValue = GetFieldValue(fieldId);
            
            if (string.IsNullOrEmpty(fieldValue))
            {
                return new List<string>();
            }

            // Try to format as autocomplete data if it looks like JSON
            if (fieldValue.StartsWith("{") || fieldValue.StartsWith("["))
            {
                return FormatAutocompleteValuesList(fieldValue);
            }

            return new List<string> { fieldValue };
        }

        public string GetFieldItemLabel(string fieldId)
        {
            // Find the field in the template
            var field = Template?.TaskGroups?
                .SelectMany(g => g.Tasks)
                .SelectMany(t => t.Pages)
                .SelectMany(p => p.Fields)
                .FirstOrDefault(f => f.FieldId == fieldId);

            if (field?.ComplexField != null)
            {
                try
                {
                    var complexField = JsonSerializer.Deserialize<Dictionary<string, object>>(field.ComplexField);
                    if (complexField?.ContainsKey("properties") == true)
                    {
                        var properties = JsonSerializer.Deserialize<Dictionary<string, object>>(complexField["properties"].ToString());
                        if (properties?.ContainsKey("label") == true)
                        {
                            return properties["label"].ToString();
                        }
                    }
                }
                catch
                {
                    // If parsing fails, return default
                }
            }

            // Default label if not found in properties
            return "Item";
        }

        public bool IsFieldAllowMultiple(string fieldId)
        {
            // Find the field in the template
            var field = Template?.TaskGroups?
                .SelectMany(g => g.Tasks)
                .SelectMany(t => t.Pages)
                .SelectMany(p => p.Fields)
                .FirstOrDefault(f => f.FieldId == fieldId);

            if (field?.ComplexField != null)
            {
                try
                {
                    var complexField = JsonSerializer.Deserialize<Dictionary<string, object>>(field.ComplexField);
                    if (complexField?.ContainsKey("properties") == true)
                    {
                        var properties = JsonSerializer.Deserialize<Dictionary<string, object>>(complexField["properties"].ToString());
                        if (properties?.ContainsKey("allowMultiple") == true)
                        {
                            return bool.Parse(properties["allowMultiple"].ToString());
                        }
                    }
                }
                catch
                {
                    // If parsing fails, return default
                }
            }

            return false; // Default to single selection
        }

        private List<string> FormatAutocompleteValuesList(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return new List<string>();
            }

            try
            {
                using (var doc = JsonDocument.Parse(value))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var displayValues = new List<string>();
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            displayValues.Add(FormatSingleAutocompleteValue(element));
                        }
                        return displayValues;
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        return new List<string> { FormatSingleAutocompleteValue(doc.RootElement) };
                    }
                }
            }
            catch
            {
                // If not JSON, return as single item
            }

            return new List<string> { value };
        }

        private string FormatAutocompleteValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            try
            {
                using (var doc = JsonDocument.Parse(value))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var displayValues = new List<string>();
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            displayValues.Add(FormatSingleAutocompleteValue(element));
                        }
                        return string.Join("<br />", displayValues);
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        return FormatSingleAutocompleteValue(doc.RootElement);
                    }
                }
            }
            catch
            {
                // If not JSON, return as is
            }

            return value;
        }

        private string FormatSingleAutocompleteValue(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                string name = "";
                string ukprn = "";

                if (element.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String)
                {
                    name = nameProperty.GetString() ?? "";
                }

                if (element.TryGetProperty("ukprn", out var ukprnProperty))
                {
                    if (ukprnProperty.ValueKind == JsonValueKind.String)
                    {
                        ukprn = ukprnProperty.GetString() ?? "";
                    }
                    else if (ukprnProperty.ValueKind == JsonValueKind.Number)
                    {
                        ukprn = ukprnProperty.GetInt64().ToString();
                    }
                }

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(ukprn))
                {
                    return $"{System.Web.HttpUtility.HtmlEncode(name)} (UKPRN: {System.Web.HttpUtility.HtmlEncode(ukprn)})";
                }
                else if (!string.IsNullOrEmpty(name))
                {
                    return System.Web.HttpUtility.HtmlEncode(name);
                }
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                return System.Web.HttpUtility.HtmlEncode(element.GetString() ?? "");
            }

            return System.Web.HttpUtility.HtmlEncode(element.ToString());
        }

        public bool HasFieldValue(string fieldId)
        {
            var value = GetFieldValue(fieldId);
            return !string.IsNullOrWhiteSpace(value);
        }

        public Domain.Models.TaskStatus GetTaskStatusFromSession(string taskId)
        {
            return CalculateTaskStatus(taskId);
        }

        /// <summary>
        /// Calculate task status based on actual form data completion
        /// </summary>
        private Domain.Models.TaskStatus CalculateTaskStatus(string taskId)
        {
            // If application is submitted, all tasks are completed
            if (ApplicationStatus.Equals("Submitted", StringComparison.OrdinalIgnoreCase))
            {
                return Domain.Models.TaskStatus.Completed;
            }
            
            // First check if task is explicitly marked as completed
            if (ApplicationId.HasValue)
            {
                var sessionKey = $"TaskStatus_{ApplicationId.Value}_{taskId}";
                var statusString = HttpContext.Session.GetString(sessionKey);
                
                if (!string.IsNullOrEmpty(statusString) && 
                    Enum.TryParse<Domain.Models.TaskStatus>(statusString, out var explicitStatus) &&
                    explicitStatus == Domain.Models.TaskStatus.Completed)
                {
                    return Domain.Models.TaskStatus.Completed;
                }
            }
            
            // Get all form data from FormData dictionary 
            var formData = FormData ?? new Dictionary<string, object>();
            
            // Find the task in the template
            var task = Template?.TaskGroups?
                .SelectMany(g => g.Tasks)
                .FirstOrDefault(t => t.TaskId == taskId);
                
            if (task == null)
            {
                return Domain.Models.TaskStatus.NotStarted;
            }
            
            // Check if any fields in this task have been completed
            var taskFieldIds = task.Pages
                .SelectMany(p => p.Fields)
                .Select(f => f.FieldId)
                .ToList();
                
            var hasAnyFieldCompleted = taskFieldIds.Any(fieldId => 
                formData.ContainsKey(fieldId) && 
                !string.IsNullOrWhiteSpace(formData[fieldId]?.ToString()));
            
            if (hasAnyFieldCompleted)
            {
                return Domain.Models.TaskStatus.InProgress;
            }
            
            return Domain.Models.TaskStatus.NotStarted;
        }

        private void LoadApplicationStatus()
        {
            if (ApplicationId.HasValue)
            {
                var statusKey = $"ApplicationStatus_{ApplicationId.Value}";
                ApplicationStatus = HttpContext.Session.GetString(statusKey) ?? "InProgress";
            }
            else
            {
                ApplicationStatus = "InProgress";
            }
        }

        public bool IsApplicationEditable()
        {
            return ApplicationStatus.Equals("InProgress", StringComparison.OrdinalIgnoreCase);
        }

        private async Task SaveTaskStatusAsync(Guid applicationId, string taskId, Domain.Models.TaskStatus status)
        {
            // Save task status to session
            _applicationResponseService.SaveTaskStatusToSession(applicationId, taskId, status.ToString(), HttpContext.Session);
            
            // Save all accumulated data (including task status) to API
            var formData = new Dictionary<string, object>(); // Empty form data since we're just updating task status
            await _applicationResponseService.SaveApplicationResponseAsync(applicationId, formData, HttpContext.Session);
        }

        private async Task EnsureApplicationIdAsync()
        {
            // First check if we have template schema stored in session for this reference
            var templateSchemaKey = $"TemplateSchema_{ReferenceNumber}";
            var templateVersionIdKey = $"TemplateVersionId_{ReferenceNumber}";
            var templateVersionNoKey = $"TemplateVersionNo_{ReferenceNumber}";
            var storedTemplateSchema = HttpContext.Session.GetString(templateSchemaKey);
            var storedTemplateVersionId = HttpContext.Session.GetString(templateVersionIdKey);
            var storedTemplateId = HttpContext.Session.GetString("TemplateId");
            var storedTemplateVersionNo = HttpContext.Session.GetString(templateVersionNoKey);

            // Check if we have basic application data in session
            var applicationIdString = HttpContext.Session.GetString("ApplicationId");
            var sessionReference = HttpContext.Session.GetString("ApplicationReference");

            if (!string.IsNullOrEmpty(applicationIdString) &&
                !string.IsNullOrEmpty(sessionReference) &&
                sessionReference == ReferenceNumber)
            {
                if (Guid.TryParse(applicationIdString, out var sessionAppId))
                {
                    ApplicationId = sessionAppId;
                    
                    // If we have template schema in session, create a minimal ApplicationDto
                    if (!string.IsNullOrEmpty(storedTemplateSchema) && !string.IsNullOrEmpty(storedTemplateVersionId))
                    {
                        _currentApplication = new ApplicationDto
                        {
                            ApplicationId = sessionAppId,
                            ApplicationReference = sessionReference,
                            TemplateVersionId = Guid.Parse(storedTemplateVersionId),
                            TemplateSchema = new TemplateSchemaDto
                            {
                                JsonSchema = storedTemplateSchema,
                                TemplateVersionId = new Guid(storedTemplateVersionId),
                                TemplateId = new Guid(storedTemplateId),
                                VersionNumber = storedTemplateVersionNo ?? String.Empty

                            }
                        };

                        _logger.LogDebug("Using cached template schema for application {ApplicationId} with template version {TemplateVersionId}", 
                            sessionAppId, storedTemplateVersionId);
                        return;
                    }
                    else
                    {
                        // For newly created applications, we don't have template schema
                        _logger.LogDebug("Using session-based application {ApplicationId} (no template schema available)", sessionAppId);
                        return;
                    }
                }
            }

            // If not in session or incomplete data, fetch from API
            try
            {
                var application = await _applicationsClient.GetApplicationByReferenceAsync(ReferenceNumber);

                if (application != null)
                {
                    ApplicationId = application.ApplicationId;
                    _currentApplication = application; // Store for template loading
                    
                    // Store application data in session for future use
                    HttpContext.Session.SetString("ApplicationId", application.ApplicationId.ToString());
                    HttpContext.Session.SetString("ApplicationReference", application.ApplicationReference);

                    // Store template schema in session for future use
                    if (application.TemplateSchema?.JsonSchema != null)
                    {
                        HttpContext.Session.SetString(templateSchemaKey, application.TemplateSchema.JsonSchema);
                        HttpContext.Session.SetString(templateVersionIdKey, application.TemplateVersionId.ToString());
                        HttpContext.Session.SetString(templateVersionNoKey, application.TemplateSchema.VersionNumber);

                        _logger.LogDebug("Cached template schema for reference {ReferenceNumber} with template version {TemplateVersionId}", 
                            ReferenceNumber, application.TemplateVersionId);
                    }

                    // Store application status in session
                    if (application.Status != null)
                    {
                        var statusKey = $"ApplicationStatus_{application.ApplicationId}";
                        HttpContext.Session.SetString(statusKey, application.Status.ToString());
                    }
                    // Load existing response data into session for existing applications
                    await LoadResponseDataIntoSessionAsync(application);
                    
                    _logger.LogDebug("Loaded application {ApplicationId} from API with template version {TemplateVersionId}", 
                        application.ApplicationId, application.TemplateVersionId);
                    return;
                }
                else
                {
                    _logger.LogWarning("Could not find application with reference {ReferenceNumber}", ReferenceNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve application information from API for reference {ReferenceNumber}", ReferenceNumber);
            }

            _logger.LogWarning("Could not determine ApplicationId for reference {ReferenceNumber}", ReferenceNumber);
        }

        private async Task LoadResponseDataIntoSessionAsync(ApplicationDto application)
        {
            if (application.LatestResponse?.ResponseBody == null)
            {
                _logger.LogInformation("No existing response data found for application {ApplicationReference}", application.ApplicationReference);
                return;
            }

            try
            {
                _logger.LogInformation("Loading response data for application {ApplicationReference}.",
                    application.ApplicationReference);

                var responseJson = application.LatestResponse.ResponseBody;

                // Parse the response body JSON
                var responseData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);

                if (responseData == null)
                {
                    _logger.LogWarning("Failed to parse response JSON for application {ApplicationReference}", application.ApplicationReference);
                    return;
                }

                var formDataDict = new Dictionary<string, object>();

                foreach (var kvp in responseData)
                {
                    try
                    {
                        // Handle both simple and complex field structures
                        if (kvp.Value.ValueKind == JsonValueKind.Object && kvp.Value.TryGetProperty("value", out var valueElement))
                        {
                            // Complex structure: {"field1": {"value": "actual_value", "completed": true}}
                            formDataDict[kvp.Key] = GetJsonElementValue(valueElement);
                        }
                        else
                        {
                            // Simple structure: {"field1": "actual_value"}
                            formDataDict[kvp.Key] = GetJsonElementValue(kvp.Value);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process field {FieldName} for application {ApplicationReference}", 
                            kvp.Key, application.ApplicationReference);
                    }
                }

                // Store in session using the same key structure as form submission
                _applicationResponseService.StoreFormDataInSession(formDataDict, HttpContext.Session);
                _applicationResponseService.SetCurrentAccumulatedApplicationId(application.ApplicationId, HttpContext.Session);

                _logger.LogInformation("Successfully loaded {FieldCount} fields from API into session for application {ApplicationReference}", 
                    formDataDict.Count, application.ApplicationReference);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load response data for application {ApplicationReference}: {ErrorMessage}", 
                    application.ApplicationReference, ex.Message);
            }
        }

        private object GetJsonElementValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    return element.GetDecimal();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null;
                default:
                    return element.ToString();
            }
        }
    }
} 