using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Services;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics.CodeAnalysis;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Web.Pages.Shared
{
    /// <summary>
    /// Base class for form-related PageModels containing common properties and functionality
    /// </summary>
    [ExcludeFromCodeCoverage]
    public abstract class BaseFormPageModel(
        IFieldRendererService renderer,
        IApplicationResponseService applicationResponseService,
        IFieldFormattingService fieldFormattingService,
        ITemplateManagementService templateManagementService,
        IApplicationStateService applicationStateService,
        ILogger logger)
        : PageModel
    {
        // Common Properties
        public FormTemplate Template { get; set; }
        [BindProperty(SupportsGet = true, Name = "referenceNumber")] public string ReferenceNumber { get; set; }
        public string TemplateId { get; set; }
        public Guid? ApplicationId { get; set; }
        public Dictionary<string, object> FormData { get; set; } = new();
        public string ApplicationStatus { get; set; } = "In progress";

        // Current application data for template schema access
        protected ApplicationDto? CurrentApplication { get; set; }

        // Common Services (injected via constructor in derived classes)
        protected readonly IFieldRendererService _renderer = renderer;
        protected readonly IApplicationResponseService _applicationResponseService = applicationResponseService;
        protected readonly IFieldFormattingService _fieldFormattingService = fieldFormattingService;
        protected readonly ITemplateManagementService _templateManagementService = templateManagementService;
        protected readonly IApplicationStateService _applicationStateService = applicationStateService;
        protected readonly ILogger _logger = logger;

        #region Common Template and Application Management

        /// <summary>
        /// Ensures application ID is loaded from session or API
        /// </summary>
        protected async Task EnsureApplicationIdAsync()
        {
            try
            {
                _logger.LogDebug("EnsureApplicationIdAsync: Starting for ReferenceNumber: {ReferenceNumber}", ReferenceNumber);
                
                var (applicationId, application) = await _applicationStateService.EnsureApplicationIdAsync(ReferenceNumber, HttpContext.Session);
                ApplicationId = applicationId;
                CurrentApplication = application;
                
                _logger.LogDebug("EnsureApplicationIdAsync: Completed - ApplicationId: {ApplicationId}, Application null: {ApplicationNull}", 
                    applicationId, application == null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EnsureApplicationIdAsync: Failed for ReferenceNumber: {ReferenceNumber}", ReferenceNumber);
                throw;
            }
        }

        /// <summary>
        /// Loads the appropriate template based on application context
        /// </summary>
        protected async Task LoadTemplateAsync()
        {
            try
            {
                _logger.LogDebug("LoadTemplateAsync: Starting with TemplateId: {TemplateId}", TemplateId);
                
                if (string.IsNullOrEmpty(TemplateId))
                {
                    _logger.LogError("TemplateId is null or empty when trying to load template");
                    throw new InvalidOperationException("TemplateId is required to load template");
                }

                _logger.LogDebug("Loading template with ID: {TemplateId}, CurrentApplication null: {ApplicationNull}", 
                    TemplateId, CurrentApplication == null);
                
                _logger.LogDebug("LoadTemplateAsync: Calling _templateManagementService.LoadTemplateAsync");
                Template = await _templateManagementService.LoadTemplateAsync(TemplateId, CurrentApplication);
                _logger.LogDebug("LoadTemplateAsync: _templateManagementService.LoadTemplateAsync completed - Template null: {TemplateNull}", Template == null);
                
                if (Template == null)
                {
                    _logger.LogError("Template loading returned null for TemplateId: {TemplateId}", TemplateId);
                    throw new InvalidOperationException($"Failed to load template with ID: {TemplateId}");
                }
                
                _logger.LogDebug("Successfully loaded template: {TemplateName}", Template.TemplateName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load template with ID: {TemplateId}", TemplateId);
                throw;
            }
        }

        /// <summary>
        /// Loads form data from session
        /// </summary>
        protected void LoadFormDataFromSession()
        {
            FormData = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session);
        }

        /// <summary>
        /// Loads application status from session
        /// </summary>
        protected void LoadApplicationStatus()
        {
            ApplicationStatus = _applicationStateService.GetApplicationStatus(ApplicationId, HttpContext.Session);
        }

        /// <summary>
        /// Checks if the application is editable
        /// </summary>
        public bool IsApplicationEditable()
        {
            return _applicationStateService.IsApplicationEditable(ApplicationStatus);
        }

        #endregion

        #region Field Value Methods (delegating to service)

        /// <summary>
        /// Gets the raw field value from form data
        /// </summary>
        public string GetFieldValue(string fieldId)
        {
            return _fieldFormattingService.GetFieldValue(fieldId, FormData);
        }

        /// <summary>
        /// Gets formatted field value for display
        /// </summary>
        public string GetFormattedFieldValue(string fieldId)
        {
            return _fieldFormattingService.GetFormattedFieldValue(fieldId, FormData);
        }

        /// <summary>
        /// Gets formatted field values as a list
        /// </summary>
        public List<string> GetFormattedFieldValues(string fieldId)
        {
            return _fieldFormattingService.GetFormattedFieldValues(fieldId, FormData);
        }

        /// <summary>
        /// Gets the label for field items
        /// </summary>
        public string GetFieldItemLabel(string fieldId)
        {
            return _fieldFormattingService.GetFieldItemLabel(fieldId, Template);
        }

        /// <summary>
        /// Checks if a field allows multiple selections
        /// </summary>
        public bool IsFieldAllowMultiple(string fieldId)
        {
            return _fieldFormattingService.IsFieldAllowMultiple(fieldId, Template);
        }

        /// <summary>
        /// Checks if a field has any value
        /// </summary>
        public bool HasFieldValue(string fieldId)
        {
            return _fieldFormattingService.HasFieldValue(fieldId, FormData);
        }

        #endregion

        #region Task Status Methods (delegating to service)

        /// <summary>
        /// Gets task status from session calculation
        /// </summary>
        public Domain.Models.TaskStatus GetTaskStatusFromSession(string taskId)
        {
            return _applicationStateService.CalculateTaskStatus(taskId, Template, FormData, ApplicationId, HttpContext.Session, ApplicationStatus);
        }

        /// <summary>
        /// Checks if all tasks are completed
        /// </summary>
        public bool AreAllTasksCompleted()
        {
            return _applicationStateService.AreAllTasksCompleted(Template, FormData, ApplicationId, HttpContext.Session, ApplicationStatus);
        }

        /// <summary>
        /// Gets the CSS class for task status display
        /// </summary>
        public string GetTaskStatusDisplayClass(Domain.Models.TaskStatus status)
        {
            return status switch
            {
                Domain.Models.TaskStatus.Completed => "govuk-tag--green",
                Domain.Models.TaskStatus.InProgress => "govuk-tag--blue",
                Domain.Models.TaskStatus.NotStarted => "govuk-tag--grey",
                Domain.Models.TaskStatus.CannotStartYet => "govuk-tag--orange",
                _ => "govuk-tag--grey"
            };
        }

        /// <summary>
        /// Gets the display text for task status
        /// </summary>
        public string GetTaskStatusDisplayText(Domain.Models.TaskStatus status)
        {
            return status switch
            {
                Domain.Models.TaskStatus.Completed => "Completed",
                Domain.Models.TaskStatus.InProgress => "In progress",
                Domain.Models.TaskStatus.NotStarted => "Not started",
                Domain.Models.TaskStatus.CannotStartYet => "Cannot start yet",
                _ => "Not started"
            };
        }

        #endregion

        #region Template Navigation Helpers

        /// <summary>
        /// Finds and initializes current task from template
        /// </summary>
        protected (TaskGroup Group, Domain.Models.Task Task) InitializeCurrentTask(string taskId)
        {
            return _templateManagementService.FindTask(Template, taskId);
        }

        /// <summary>
        /// Finds and initializes current page from template
        /// </summary>
        protected (TaskGroup Group, Domain.Models.Task Task, Domain.Models.Page Page) InitializeCurrentPage(string pageId)
        {
            return _templateManagementService.FindPage(Template, pageId);
        }

        #endregion

        #region Common Initialization Pattern

        /// <summary>
        /// Common initialization pattern used by most form pages
        /// </summary>
        protected async Task CommonInitializationAsync()
        {
            try
            {
                _logger.LogDebug("CommonInitializationAsync: Starting for ReferenceNumber: {ReferenceNumber}", ReferenceNumber);
                
                // Log session state before loading TemplateId
                var sessionKeys = HttpContext.Session.Keys.ToList();
                _logger.LogDebug("CommonInitializationAsync: Session keys before TemplateId load: {SessionKeys}", string.Join(", ", sessionKeys));
                
                TemplateId = HttpContext.Session.GetString("TemplateId") ?? string.Empty;
                _logger.LogDebug("CommonInitializationAsync: TemplateId loaded from session: {TemplateId}", TemplateId);
                
                if (string.IsNullOrEmpty(TemplateId))
                {
                    _logger.LogError("TemplateId not found in session. Session keys: {SessionKeys}", 
                        string.Join(", ", HttpContext.Session.Keys));
                    throw new InvalidOperationException("TemplateId is required but not found in session");
                }
                
                _logger.LogDebug("CommonInitializationAsync: TemplateId validation passed: {TemplateId}", TemplateId);
                
                _logger.LogDebug("CommonInitializationAsync: Calling EnsureApplicationIdAsync");
                await EnsureApplicationIdAsync();
                _logger.LogDebug("CommonInitializationAsync: EnsureApplicationIdAsync completed - ApplicationId: {ApplicationId}", ApplicationId);
                
                _logger.LogDebug("CommonInitializationAsync: Calling LoadTemplateAsync");
                await LoadTemplateAsync();
                _logger.LogDebug("CommonInitializationAsync: LoadTemplateAsync completed - Template null: {TemplateNull}", Template == null);
                
                _logger.LogDebug("CommonInitializationAsync: Calling LoadFormDataFromSession");
                LoadFormDataFromSession();
                _logger.LogDebug("CommonInitializationAsync: LoadFormDataFromSession completed");
                
                _logger.LogDebug("CommonInitializationAsync: Calling LoadApplicationStatus");
                LoadApplicationStatus();
                _logger.LogDebug("CommonInitializationAsync: LoadApplicationStatus completed");
                
                _logger.LogDebug("Common initialization completed successfully for TemplateId: {TemplateId}", TemplateId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed during common initialization for TemplateId: {TemplateId}, ReferenceNumber: {ReferenceNumber}", TemplateId, ReferenceNumber);
                throw;
            }
        }

        #endregion
    }
} 