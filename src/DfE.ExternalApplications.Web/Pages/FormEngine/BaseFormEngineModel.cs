using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Pages.Shared;
using DfE.ExternalApplications.Web.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.CodeAnalysis;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Web.Pages.FormEngine
{
    /// <summary>
    /// Base class for form engine page models containing common functionality
    /// </summary>
    [ExcludeFromCodeCoverage]
    public abstract class BaseFormEngineModel(
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
        ILogger logger)
        : BaseFormPageModel(renderer, applicationResponseService, fieldFormattingService, templateManagementService,
            applicationStateService, logger)
    {
        // Form Engine Services
        protected readonly IFormStateManager _formStateManager = formStateManager;
        protected readonly IFormNavigationService _formNavigationService = formNavigationService;
        protected readonly IFormDataManager _formDataManager = formDataManager;
        protected readonly IFormValidationOrchestrator _formValidationOrchestrator = formValidationOrchestrator;
        protected readonly IFormConfigurationService _formConfigurationService = formConfigurationService;

        // Common Properties
        public FormState CurrentFormState { get; set; }
        public TaskGroup CurrentGroup { get; set; }
        public Domain.Models.Task CurrentTask { get; set; }
        public Domain.Models.Page CurrentPage { get; set; }
        
        // URL Parameters (to be set by derived classes)
        [BindProperty(SupportsGet = true, Name = "taskId")] public string TaskId { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true, Name = "pageId")] public string CurrentPageId { get; set; } = string.Empty;
        // Optional sub-flow tokens; nullable so model binding does not require them on normal pages
        [BindProperty(SupportsGet = true, Name = "flowId")] public string? FlowId { get; set; }
        [BindProperty(SupportsGet = true, Name = "instanceId")] public string? InstanceId { get; set; }

        /// <summary>
        /// Gets the current form state based on the URL parameters
        /// </summary>
        /// <returns>The current form state</returns>
        protected FormState GetCurrentFormState()
        {
            return _formStateManager.GetCurrentState(ReferenceNumber, TaskId, CurrentPageId);
        }

        /// <summary>
        /// Gets the back link URL for the current context
        /// </summary>
        /// <returns>The back link URL</returns>
        protected string GetBackLinkUrl()
        {
            return _formNavigationService.GetBackLinkUrl(CurrentPageId, TaskId, ReferenceNumber);
        }

        /// <summary>
        /// Exposes the back link URL to Razor views that cannot call protected methods.
        /// </summary>
        public string BackLinkUrl => GetBackLinkUrl();

        /// <summary>
        /// Gets the task summary URL for the current task
        /// </summary>
        /// <returns>The task summary URL</returns>
        protected string GetTaskSummaryUrl()
        {
            return _formNavigationService.GetTaskSummaryUrl(TaskId, ReferenceNumber);
        }

        /// <summary>
        /// Gets the application preview URL
        /// </summary>
        /// <returns>The application preview URL</returns>
        protected string GetApplicationPreviewUrl()
        {
            return _formNavigationService.GetApplicationPreviewUrl(ReferenceNumber);
        }

        /// <summary>
        /// Gets the task list URL
        /// </summary>
        /// <returns>The task list URL</returns>
        protected string GetTaskListUrl()
        {
            return _formNavigationService.GetTaskListUrl(ReferenceNumber);
        }

        /// <summary>
        /// Validates the current page using the validation orchestrator
        /// </summary>
        /// <param name="page">The page to validate</param>
        /// <param name="data">The form data</param>
        /// <returns>True if validation passes</returns>
        protected bool ValidateCurrentPage(Domain.Models.Page page, Dictionary<string, object> data)
        {
            return _formValidationOrchestrator.ValidatePage(page, data, ModelState, Template);
        }

        /// <summary>
        /// Validates the current task using the validation orchestrator
        /// </summary>
        /// <param name="task">The task to validate</param>
        /// <param name="data">The form data</param>
        /// <returns>True if validation passes</returns>
        protected bool ValidateCurrentTask(Domain.Models.Task task, Dictionary<string, object> data)
        {
            return _formValidationOrchestrator.ValidateTask(task, data, ModelState, Template);
        }

        /// <summary>
        /// Gets the form configuration for the current template
        /// </summary>
        /// <returns>The form configuration</returns>
        protected FormConfiguration GetFormConfiguration()
        {
            return _formConfigurationService.GetFormConfiguration(TemplateId);
        }

        /// <summary>
        /// Gets the default form settings
        /// </summary>
        /// <returns>The default form settings</returns>
        protected FormSettings GetDefaultFormSettings()
        {
            return _formConfigurationService.GetDefaultFormSettings();
        }

        /// <summary>
        /// Common initialization for form engine pages
        /// </summary>
        /// <returns>A task representing the asynchronous operation</returns>
        protected async Task CommonFormEngineInitializationAsync()
        {
            
            
            try
            {
                await CommonInitializationAsync();
                
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CommonFormEngineInitializationAsync - Error in CommonInitializationAsync");
                throw;
            }
            
            try
            {
                CurrentFormState = GetCurrentFormState();
                
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CommonFormEngineInitializationAsync - Error getting current form state");
                throw;
            }
            
            
        }

        /// <summary>
        /// Checks if the current form state should show the task list
        /// </summary>
        /// <returns>True if task list should be shown</returns>
        protected bool ShouldShowTaskList()
        {
            return _formStateManager.ShouldShowTaskList(CurrentPageId);
        }

        /// <summary>
        /// Checks if the current form state should show the task summary
        /// </summary>
        /// <returns>True if task summary should be shown</returns>
        protected bool ShouldShowTaskSummary()
        {
            return _formStateManager.ShouldShowTaskSummary(TaskId, CurrentPageId);
        }

        /// <summary>
        /// Checks if the current form state should show the application preview
        /// </summary>
        /// <returns>True if application preview should be shown</returns>
        protected bool ShouldShowApplicationPreview()
        {
            return _formStateManager.ShouldShowApplicationPreview(CurrentPageId);
        }
    }
}
