using System.Text.Json;
using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using Microsoft.AspNetCore.Http;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Infrastructure.Services
{
    /// <summary>
    /// Service for managing user notifications stored in session
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class NotificationService : INotificationService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private const string SessionKey = "UserNotifications";

        public NotificationService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        private ISession Session => _httpContextAccessor.HttpContext?.Session 
            ?? throw new InvalidOperationException("Session is not available");

        private List<Notification> GetNotifications()
        {
            var notificationsJson = Session.GetString(SessionKey);
            if (string.IsNullOrEmpty(notificationsJson))
            {
                return new List<Notification>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<Notification>>(notificationsJson) ?? new List<Notification>();
            }
            catch
            {
                // If deserialization fails, return empty list and clear corrupted data
                Session.Remove(SessionKey);
                return new List<Notification>();
            }
        }

        private void SaveNotifications(List<Notification> notifications)
        {
            var notificationsJson = JsonSerializer.Serialize(notifications);
            Session.SetString(SessionKey, notificationsJson);
        }

        public void AddSuccess(string message, string? context = null, string? category = null, 
            bool autoDismiss = true, int autoDismissSeconds = 5)
        {
            AddNotification(message, NotificationType.Success, context, category, autoDismiss, autoDismissSeconds);
        }

        public void AddError(string message, string? context = null, string? category = null, 
            bool autoDismiss = false, int autoDismissSeconds = 10)
        {
            AddNotification(message, NotificationType.Error, context, category, autoDismiss, autoDismissSeconds);
        }

        public void AddInfo(string message, string? context = null, string? category = null, 
            bool autoDismiss = true, int autoDismissSeconds = 5)
        {
            AddNotification(message, NotificationType.Info, context, category, autoDismiss, autoDismissSeconds);
        }

        public void AddWarning(string message, string? context = null, string? category = null, 
            bool autoDismiss = true, int autoDismissSeconds = 7)
        {
            AddNotification(message, NotificationType.Warning, context, category, autoDismiss, autoDismissSeconds);
        }

        private void AddNotification(string message, NotificationType type, string? context, 
            string? category, bool autoDismiss, int autoDismissSeconds)
        {
            var notifications = GetNotifications();
            
            // Remove any existing notifications with the same context to prevent duplicates
            if (!string.IsNullOrEmpty(context))
            {
                notifications.RemoveAll(n => n.Context == context);
            }

            var notification = new Notification
            {
                Message = message,
                Type = type,
                Context = context,
                Category = category,
                AutoDismiss = autoDismiss,
                AutoDismissSeconds = autoDismissSeconds,
                CreatedAt = DateTime.UtcNow
            };

            notifications.Add(notification);
            
            // Keep only the latest 50 notifications to prevent session bloat
            if (notifications.Count > 50)
            {
                notifications = notifications.OrderByDescending(n => n.CreatedAt).Take(50).ToList();
            }

            SaveNotifications(notifications);
        }

        public List<Notification> GetUnreadNotifications()
        {
            return GetNotifications().Where(n => !n.IsRead).OrderByDescending(n => n.CreatedAt).ToList();
        }

        public List<Notification> GetAllNotifications()
        {
            return GetNotifications().OrderByDescending(n => n.CreatedAt).ToList();
        }

        public void MarkAsRead(string notificationId)
        {
            var notifications = GetNotifications();
            var notification = notifications.FirstOrDefault(n => n.Id == notificationId);
            if (notification != null)
            {
                notification.IsRead = true;
                SaveNotifications(notifications);
            }
        }

        public void RemoveNotification(string notificationId)
        {
            var notifications = GetNotifications();
            notifications.RemoveAll(n => n.Id == notificationId);
            SaveNotifications(notifications);
        }

        public void ClearAllNotifications()
        {
            Session.Remove(SessionKey);
        }

        public void ClearNotificationsByCategory(string category)
        {
            var notifications = GetNotifications();
            notifications.RemoveAll(n => n.Category == category);
            SaveNotifications(notifications);
        }

        public void ClearNotificationsByContext(string context)
        {
            var notifications = GetNotifications();
            notifications.RemoveAll(n => n.Context == context);
            SaveNotifications(notifications);
        }
    }
}