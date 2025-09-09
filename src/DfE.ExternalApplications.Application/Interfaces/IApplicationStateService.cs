using DfE.ExternalApplications.Domain.Models;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using Microsoft.AspNetCore.Http;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Application.Interfaces
{
    /// <summary>
    /// Service for managing application state, status, and session data
    /// </summary>
    public interface IApplicationStateService
    {
        /// <summary>
        /// Ensures application ID is loaded from session or API
        /// </summary>
        Task<(Guid? ApplicationId, ApplicationDto? Application)> EnsureApplicationIdAsync(string referenceNumber, ISession session);

        /// <summary>
        /// Loads response data from API into session
        /// </summary>
        Task LoadResponseDataIntoSessionAsync(ApplicationDto application, ISession session);

        /// <summary>
        /// Gets application status from session or default
        /// </summary>
        string GetApplicationStatus(Guid? applicationId, ISession session);

        /// <summary>
        /// Checks if application is editable based on status
        /// </summary>
        bool IsApplicationEditable(string applicationStatus);

        /// <summary>
        /// Calculates task status based on form data and explicit status
        /// </summary>
        Domain.Models.TaskStatus CalculateTaskStatus(string taskId, FormTemplate template, Dictionary<string, object> formData, Guid? applicationId, ISession session, string applicationStatus);

        /// <summary>
        /// Saves task status to session and API
        /// </summary>
        Task SaveTaskStatusAsync(Guid applicationId, string taskId, Domain.Models.TaskStatus status, ISession session);

        /// <summary>
        /// Checks if all tasks in the template are completed
        /// </summary>
        bool AreAllTasksCompleted(FormTemplate template, Dictionary<string, object> formData, Guid? applicationId, ISession session, string applicationStatus);

        /// <summary>
        /// Converts JSON element to appropriate object type
        /// </summary>
        object GetJsonElementValue(System.Text.Json.JsonElement element);
    }
} 