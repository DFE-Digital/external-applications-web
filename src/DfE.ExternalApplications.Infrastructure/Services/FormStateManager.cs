using DfE.ExternalApplications.Application.Interfaces;

namespace DfE.ExternalApplications.Infrastructure.Services
{
    /// <summary>
    /// Implementation of the form state manager that determines which view should be rendered
    /// </summary>
    public class FormStateManager : IFormStateManager
    {
        /// <summary>
        /// Gets the current form state based on the provided parameters
        /// </summary>
        /// <param name="referenceNumber">The application reference number</param>
        /// <param name="taskId">The current task ID (optional)</param>
        /// <param name="pageId">The current page ID (optional)</param>
        /// <returns>The current form state</returns>
        public FormState GetCurrentState(string referenceNumber, string taskId, string pageId)
        {
            // Confirmation routing pattern: pageId may include "confirm/..." segment
            if (!string.IsNullOrEmpty(pageId) && pageId.StartsWith("confirm/", StringComparison.OrdinalIgnoreCase))
            {
                return FormState.Confirmation;
            }
            
            // Sub-flow routing pattern: pageId may include "flow/flowId/..." segment when mapped in the Razor Page
            if (!string.IsNullOrEmpty(pageId) && pageId.StartsWith("flow/", StringComparison.OrdinalIgnoreCase))
            {
                return FormState.SubFlowPage;
            }
            // If we have a pageId, we're showing a specific form page
            if (!string.IsNullOrEmpty(pageId))
            {
                return FormState.FormPage;
            }
            
            // If we have a taskId but no pageId, we're showing the task summary
            if (!string.IsNullOrEmpty(taskId))
            {
                return FormState.TaskSummary;
            }
            
            // If we have neither taskId nor pageId, we're showing the task list
            return FormState.TaskList;
        }
        
        /// <summary>
        /// Determines if the task list should be shown
        /// </summary>
        /// <param name="pageId">The current page ID</param>
        /// <returns>True if task list should be shown</returns>
        public bool ShouldShowTaskList(string pageId)
        {
            return string.IsNullOrEmpty(pageId);
        }
        
        /// <summary>
        /// Determines if the task summary should be shown
        /// </summary>
        /// <param name="taskId">The current task ID</param>
        /// <param name="pageId">The current page ID</param>
        /// <returns>True if task summary should be shown</returns>
        public bool ShouldShowTaskSummary(string taskId, string pageId)
        {
            return !string.IsNullOrEmpty(taskId) && string.IsNullOrEmpty(pageId);
        }
        
        /// <summary>
        /// Determines if the application preview should be shown
        /// </summary>
        /// <param name="pageId">The current page ID</param>
        /// <returns>True if application preview should be shown</returns>
        public bool ShouldShowApplicationPreview(string pageId)
        {
            return !string.IsNullOrEmpty(pageId) && pageId.Equals("preview", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines if the confirmation page should be shown
        /// </summary>
        /// <param name="pageId">The current page ID</param>
        /// <returns>True if confirmation page should be shown</returns>
        public bool ShouldShowConfirmation(string pageId)
        {
            return !string.IsNullOrEmpty(pageId) && pageId.StartsWith("confirm/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines if the collection flow summary should be shown
        /// </summary>
        /// <param name="task">The task to check</param>
        /// <returns>True if collection flow summary should be shown</returns>
        public bool ShouldShowCollectionFlowSummary(Domain.Models.Task task)
        {
            return task?.Summary?.Mode?.Equals("multiCollectionFlow", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Determines if the current page is within a sub-flow
        /// </summary>
        /// <param name="flowId">The flow ID</param>
        /// <param name="pageId">The current page ID</param>
        /// <returns>True if the page is within a sub-flow</returns>
        public bool IsInSubFlow(string flowId, string pageId)
        {
            return !string.IsNullOrEmpty(flowId) && !string.IsNullOrEmpty(pageId) && pageId.StartsWith("flow/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
