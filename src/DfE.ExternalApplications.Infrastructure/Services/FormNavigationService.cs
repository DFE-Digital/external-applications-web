using DfE.ExternalApplications.Application.Interfaces;

namespace DfE.ExternalApplications.Infrastructure.Services
{
    /// <summary>
    /// Implementation of the form navigation service that handles URL generation and navigation logic
    /// </summary>
    public class FormNavigationService : IFormNavigationService
    {
        /// <summary>
        /// Gets the URL for the next page in the form
        /// </summary>
        /// <param name="currentPageId">The current page ID</param>
        /// <param name="taskId">The current task ID</param>
        /// <param name="referenceNumber">The application reference number</param>
        /// <returns>The URL for the next page</returns>
        public string GetNextPageUrl(string currentPageId, string taskId, string referenceNumber)
        {
            // For now, we'll redirect to task summary after saving a page
            return GetTaskSummaryUrl(taskId, referenceNumber);
        }
        
        /// <summary>
        /// Gets the URL for the previous page in the form
        /// </summary>
        /// <param name="currentPageId">The current page ID</param>
        /// <param name="taskId">The current task ID</param>
        /// <param name="referenceNumber">The application reference number</param>
        /// <returns>The URL for the previous page</returns>
        public string GetPreviousPageUrl(string currentPageId, string taskId, string referenceNumber)
        {
            // For now, we'll go back to task summary
            return GetTaskSummaryUrl(taskId, referenceNumber);
        }
        
        /// <summary>
        /// Gets the URL for the task summary page
        /// </summary>
        /// <param name="taskId">The task ID</param>
        /// <param name="referenceNumber">The application reference number</param>
        /// <returns>The URL for the task summary</returns>
        public string GetTaskSummaryUrl(string taskId, string referenceNumber)
        {
            return $"/applications/{referenceNumber}/{taskId}";
        }
        
        /// <summary>
        /// Gets the URL for the application preview page
        /// </summary>
        /// <param name="referenceNumber">The application reference number</param>
        /// <returns>The URL for the application preview</returns>
        public string GetApplicationPreviewUrl(string referenceNumber)
        {
            return $"/applications/{referenceNumber}/preview";
        }
        
        /// <summary>
        /// Gets the URL for the task list page
        /// </summary>
        /// <param name="referenceNumber">The application reference number</param>
        /// <returns>The URL for the task list</returns>
        public string GetTaskListUrl(string referenceNumber)
        {
            return $"/applications/{referenceNumber}";
        }
        
        /// <summary>
        /// Determines if navigation to a specific page is allowed
        /// </summary>
        /// <param name="pageId">The target page ID</param>
        /// <param name="taskId">The task ID</param>
        /// <param name="referenceNumber">The application reference number</param>
        /// <returns>True if navigation is allowed</returns>
        public bool CanNavigateToPage(string pageId, string taskId, string referenceNumber)
        {
            // For now, we'll allow navigation to any page
            // This could be enhanced with validation logic based on task completion status
            return !string.IsNullOrEmpty(pageId) && !string.IsNullOrEmpty(taskId) && !string.IsNullOrEmpty(referenceNumber);
        }
        
        /// <summary>
        /// Gets the back link URL for the current context
        /// </summary>
        /// <param name="currentPageId">The current page ID</param>
        /// <param name="taskId">The current task ID</param>
        /// <param name="referenceNumber">The application reference number</param>
        /// <returns>The back link URL</returns>
        public string GetBackLinkUrl(string currentPageId, string taskId, string referenceNumber)
        {
            // If we're on a specific page, go back to task summary
            if (!string.IsNullOrEmpty(currentPageId))
            {
                return GetTaskSummaryUrl(taskId, referenceNumber);
            }
            
            // If we're on task summary, go back to task list
            if (!string.IsNullOrEmpty(taskId))
            {
                return GetTaskListUrl(referenceNumber);
            }
            
            // Default to task list
            return GetTaskListUrl(referenceNumber);
        }

        /// <summary>
        /// Gets the next navigation target after saving a page, considering the returnToSummaryPage property
        /// </summary>
        /// <param name="currentPage">The current page that was just saved</param>
        /// <param name="currentTask">The current task</param>
        /// <param name="referenceNumber">The application reference number</param>
        /// <returns>The URL for the next navigation target</returns>
        public string GetNextNavigationTargetAfterSave(Domain.Models.Page currentPage, Domain.Models.Task currentTask, string referenceNumber)
        {
            // If the page has returnToSummaryPage set to true, go to task summary
            if (currentPage.ReturnToSummaryPage)
            {
                return GetTaskSummaryUrl(currentTask.TaskId, referenceNumber);
            }

            // Find the next page in the same task
            var nextPage = GetNextPageInTask(currentPage, currentTask);
            if (nextPage != null)
            {
                return $"/applications/{referenceNumber}/{currentTask.TaskId}/{nextPage.PageId}";
            }

            // If there's no next page in the task, go to task summary
            return GetTaskSummaryUrl(currentTask.TaskId, referenceNumber);
        }

        /// <summary>
        /// Gets the next page in the same task, or null if there is no next page
        /// </summary>
        /// <param name="currentPage">The current page</param>
        /// <param name="currentTask">The current task</param>
        /// <returns>The next page, or null if there is no next page</returns>
        private Domain.Models.Page? GetNextPageInTask(Domain.Models.Page currentPage, Domain.Models.Task currentTask)
        {
            if (currentTask.Pages == null || !currentTask.Pages.Any())
            {
                return null;
            }

            // Find the current page index
            var currentPageIndex = currentTask.Pages.FindIndex(p => p.PageId == currentPage.PageId);
            if (currentPageIndex == -1 || currentPageIndex >= currentTask.Pages.Count - 1)
            {
                // Current page not found or it's the last page
                return null;
            }

            // Return the next page
            return currentTask.Pages[currentPageIndex + 1];
        }

        public string GetPageUrl(string taskId, string referenceNumber, string pageId)
        {
            if (string.IsNullOrWhiteSpace(pageId))
            {
                return GetTaskSummaryUrl(taskId, referenceNumber);
            }

            // If the pageId already contains a sub-flow route, return it directly under the task
            if (pageId.StartsWith("flow/", StringComparison.OrdinalIgnoreCase))
            {
                return $"/applications/{referenceNumber}/{taskId}/{pageId}";
            }

            // Regular page under the task
            return $"/applications/{referenceNumber}/{taskId}/{pageId}";
        }

        // Sub-flow helpers
        public string GetCollectionFlowSummaryUrl(string taskId, string referenceNumber)
        {
            // Uses the same route as task summary; the view model decides which summary to render
            return $"/applications/{referenceNumber}/{taskId}";
        }

        public string GetStartSubFlowUrl(string taskId, string referenceNumber, string flowId, string instanceId)
        {
            return $"/applications/{referenceNumber}/{taskId}/flow/{flowId}/{instanceId}";
        }

        public string GetSubFlowPageUrl(string taskId, string referenceNumber, string flowId, string instanceId, string pageId)
        {
            return $"/applications/{referenceNumber}/{taskId}/flow/{flowId}/{instanceId}/{pageId}";
        }

        /// <summary>
        /// Gets the URL for the confirmation page
        /// </summary>
        /// <param name="taskId">The task ID</param>
        /// <param name="referenceNumber">The application reference number</param>
        /// <param name="operation">The operation being confirmed</param>
        /// <param name="fieldId">The field ID</param>
        /// <param name="confirmationToken">The confirmation token</param>
        /// <returns>The URL for the confirmation page</returns>
        public string GetConfirmationUrl(string taskId, string referenceNumber, string operation, string fieldId, string confirmationToken)
        {
            return $"/applications/{referenceNumber}/{taskId}/confirm/{operation}/{fieldId}/{confirmationToken}";
        }
    }
}
