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
            return $"/applications/{referenceNumber}/{taskId}/summary";
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
    }
}
