using DfE.ExternalApplications.Domain.Models;

namespace DfE.ExternalApplications.Application.Interfaces
{
    /// <summary>
    /// Service for managing user notifications
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Add a success notification
        /// </summary>
        /// <param name="message">Notification message</param>
        /// <param name="context">Optional context (e.g., fieldId)</param>
        /// <param name="category">Optional category for grouping</param>
        /// <param name="autoDismiss">Whether to auto-dismiss</param>
        /// <param name="autoDismissSeconds">Auto-dismiss timeout in seconds</param>
        void AddSuccess(string message, string? context = null, string? category = null, 
            bool autoDismiss = true, int autoDismissSeconds = 5);

        /// <summary>
        /// Add an error notification
        /// </summary>
        /// <param name="message">Notification message</param>
        /// <param name="context">Optional context (e.g., fieldId)</param>
        /// <param name="category">Optional category for grouping</param>
        /// <param name="autoDismiss">Whether to auto-dismiss</param>
        /// <param name="autoDismissSeconds">Auto-dismiss timeout in seconds</param>
        void AddError(string message, string? context = null, string? category = null, 
            bool autoDismiss = false, int autoDismissSeconds = 10);

        /// <summary>
        /// Add an info notification
        /// </summary>
        /// <param name="message">Notification message</param>
        /// <param name="context">Optional context (e.g., fieldId)</param>
        /// <param name="category">Optional category for grouping</param>
        /// <param name="autoDismiss">Whether to auto-dismiss</param>
        /// <param name="autoDismissSeconds">Auto-dismiss timeout in seconds</param>
        void AddInfo(string message, string? context = null, string? category = null, 
            bool autoDismiss = true, int autoDismissSeconds = 5);

        /// <summary>
        /// Add a warning notification
        /// </summary>
        /// <param name="message">Notification message</param>
        /// <param name="context">Optional context (e.g., fieldId)</param>
        /// <param name="category">Optional category for grouping</param>
        /// <param name="autoDismiss">Whether to auto-dismiss</param>
        /// <param name="autoDismissSeconds">Auto-dismiss timeout in seconds</param>
        void AddWarning(string message, string? context = null, string? category = null, 
            bool autoDismiss = true, int autoDismissSeconds = 7);

        /// <summary>
        /// Get all unread notifications
        /// </summary>
        /// <returns>List of unread notifications</returns>
        List<Notification> GetUnreadNotifications();

        /// <summary>
        /// Get all notifications (read and unread)
        /// </summary>
        /// <returns>List of all notifications</returns>
        List<Notification> GetAllNotifications();

        /// <summary>
        /// Mark a notification as read
        /// </summary>
        /// <param name="notificationId">Notification ID</param>
        void MarkAsRead(string notificationId);

        /// <summary>
        /// Remove a notification
        /// </summary>
        /// <param name="notificationId">Notification ID</param>
        void RemoveNotification(string notificationId);

        /// <summary>
        /// Clear all notifications
        /// </summary>
        void ClearAllNotifications();

        /// <summary>
        /// Clear notifications by category
        /// </summary>
        /// <param name="category">Category to clear</param>
        void ClearNotificationsByCategory(string category);

        /// <summary>
        /// Clear notifications by context
        /// </summary>
        /// <param name="context">Context to clear</param>
        void ClearNotificationsByContext(string context);
    }
}