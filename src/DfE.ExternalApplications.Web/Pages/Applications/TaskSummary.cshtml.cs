using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Pages.Shared;
using DfE.ExternalApplications.Web.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.CodeAnalysis;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Web.Pages.Applications
{
    [ExcludeFromCodeCoverage]
    public class TaskSummaryModel(
        IFieldRendererService renderer,
        IApplicationResponseService applicationResponseService,
        IFieldFormattingService fieldFormattingService,
        ITemplateManagementService templateManagementService,
        IApplicationStateService applicationStateService,
        ILogger<TaskSummaryModel> logger)
        : BaseFormPageModel(renderer, applicationResponseService, fieldFormattingService, templateManagementService,
            applicationStateService, logger)
    {
        [BindProperty(SupportsGet = true, Name = "taskId")] public string TaskId { get; set; }
        [BindProperty] public bool IsTaskCompleted { get; set; }

        public TaskGroup CurrentGroup { get; set; }
        public Domain.Models.Task CurrentTask { get; set; }

        public async Task OnGetAsync()
        {
            await CommonInitializationAsync();
            var (group, task) = InitializeCurrentTask(TaskId);
            CurrentGroup = group;
            CurrentTask = task;
            
            // Check if task is already marked as completed from session
            IsTaskCompleted = GetTaskStatusFromSession(TaskId) == Domain.Models.TaskStatus.Completed;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await CommonInitializationAsync();
            var (group, task) = InitializeCurrentTask(TaskId);
            CurrentGroup = group;
            CurrentTask = task;

            // Prevent editing if application is not editable
            if (!IsApplicationEditable())
            {
                return RedirectToPage("/Applications/ApplicationPreview", new { referenceNumber = ReferenceNumber });
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
                    await _applicationStateService.SaveTaskStatusAsync(ApplicationId.Value, TaskId, newStatus, HttpContext.Session);
                    _logger.LogInformation("Successfully updated task status for Application {ApplicationId}, Task {TaskId} to {Status}",
                        ApplicationId.Value, TaskId, newStatus);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save task status for Application {ApplicationId}, Task {TaskId}",
                        ApplicationId.Value, TaskId);
                }
            }

            return RedirectToPage("/FormEngine/RenderForm", new { referenceNumber = ReferenceNumber });
        }
    }
} 