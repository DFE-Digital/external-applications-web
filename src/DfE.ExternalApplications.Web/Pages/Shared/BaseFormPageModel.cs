using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Services;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
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
        public string ApplicationStatus { get; set; } = "InProgress";

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
            var (applicationId, application) = await _applicationStateService.EnsureApplicationIdAsync(ReferenceNumber, HttpContext.Session);
            ApplicationId = applicationId;
            CurrentApplication = application;
        }

        /// <summary>
        /// Loads the appropriate template based on application context
        /// </summary>
        protected async Task LoadTemplateAsync()
        {
            Template = await _templateManagementService.LoadTemplateAsync(TemplateId, CurrentApplication);
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
            _logger.LogInformation("CommonInitializationAsync START - ReferenceNumber: {ReferenceNumber}", ReferenceNumber);
            
            try
            {
                TemplateId = HttpContext.Session.GetString("TemplateId") ?? string.Empty;
                _logger.LogInformation("CommonInitializationAsync - TemplateId from session: {TemplateId}", TemplateId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommonInitializationAsync - Error getting TemplateId from session");
                throw;
            }
            
            try
            {
                await EnsureApplicationIdAsync();
                _logger.LogInformation("CommonInitializationAsync - ApplicationId ensured: {ApplicationId}", ApplicationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommonInitializationAsync - Error ensuring ApplicationId");
                throw;
            }
            
            try
            {
                await LoadTemplateAsync();
                _logger.LogInformation("CommonInitializationAsync - Template loaded: {TemplateIsNull}, TemplateId: {TemplateId}", 
                    Template == null, Template?.TemplateId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommonInitializationAsync - Error loading template");
                throw;
            }
            
            try
            {
                LoadFormDataFromSession();
                _logger.LogInformation("CommonInitializationAsync - FormData loaded: {FormDataCount} entries", FormData?.Count ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommonInitializationAsync - Error loading form data from session");
                throw;
            }
            
            try
            {
                LoadApplicationStatus();
                _logger.LogInformation("CommonInitializationAsync - ApplicationStatus loaded: {ApplicationStatus}", ApplicationStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommonInitializationAsync - Error loading application status");
                throw;
            }
            
            _logger.LogInformation("CommonInitializationAsync COMPLETE");
        }

        #endregion
    }
} 