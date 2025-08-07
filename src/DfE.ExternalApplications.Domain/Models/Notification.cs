using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Domain.Models
{
    /// <summary>
    /// Represents a user notification that can be displayed in the UI
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class Notification
    {
        /// <summary>
        /// Unique identifier for the notification
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// The notification message
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Type of notification (success, error, info, warning)
        /// </summary>
        public NotificationType Type { get; set; }

        /// <summary>
        /// When the notification was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the notification has been seen/read
        /// </summary>
        public bool IsRead { get; set; } = false;

        /// <summary>
        /// Whether the notification should auto-dismiss after a timeout
        /// </summary>
        public bool AutoDismiss { get; set; } = true;

        /// <summary>
        /// Auto-dismiss timeout in seconds (default 5 seconds)
        /// </summary>
        public int AutoDismissSeconds { get; set; } = 5;

        /// <summary>
        /// Optional context information (e.g., fieldId, uploadId, etc.)
        /// </summary>
        public string? Context { get; set; }

        /// <summary>
        /// Optional category for grouping notifications
        /// </summary>
        public string? Category { get; set; }
    }

    /// <summary>
    /// Types of notifications
    /// </summary>
    public enum NotificationType
    {
        /// <summary>
        /// Success notification (green)
        /// </summary>
        Success,

        /// <summary>
        /// Error notification (red)
        /// </summary>
        Error,

        /// <summary>
        /// Information notification (blue)
        /// </summary>
        Info,

        /// <summary>
        /// Warning notification (yellow)
        /// </summary>
        Warning
    }
}