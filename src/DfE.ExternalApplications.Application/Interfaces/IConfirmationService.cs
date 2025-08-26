using DfE.ExternalApplications.Domain.Models;

namespace DfE.ExternalApplications.Application.Interfaces
{
    /// <summary>
    /// Service for managing confirmation flows for add/update/delete operations
    /// </summary>
    public interface IConfirmationService
    {
        /// <summary>
        /// Determines if a field requires confirmation before proceeding
        /// </summary>
        /// <param name="field">The field to check</param>
        /// <param name="operation">The operation being performed (add, update, delete)</param>
        /// <returns>True if confirmation is required</returns>
        bool RequiresConfirmation(Field field, ConfirmationOperation operation);

        /// <summary>
        /// Creates a confirmation model for the specified operation
        /// </summary>
        /// <param name="field">The field being operated on</param>
        /// <param name="operation">The operation being performed</param>
        /// <param name="itemData">The item data for the operation</param>
        /// <param name="taskId">The current task ID</param>
        /// <param name="referenceNumber">The application reference number</param>
        /// <returns>A confirmation model</returns>
        ConfirmationModel CreateConfirmationModel(Field field, ConfirmationOperation operation, Dictionary<string, object>? itemData, string taskId, string referenceNumber);

        /// <summary>
        /// Processes the confirmation result and returns the next action
        /// </summary>
        /// <param name="confirmationModel">The confirmation model with user's response</param>
        /// <returns>The next action to take</returns>
        ConfirmationResult ProcessConfirmation(ConfirmationModel confirmationModel);
    }

    /// <summary>
    /// Represents the type of operation requiring confirmation
    /// </summary>
    public enum ConfirmationOperation
    {
        /// <summary>
        /// Adding a new item
        /// </summary>
        Add,

        /// <summary>
        /// Updating an existing item
        /// </summary>
        Update,

        /// <summary>
        /// Deleting an existing item
        /// </summary>
        Delete
    }

    /// <summary>
    /// Model containing confirmation information
    /// </summary>
    public class ConfirmationModel
    {
        /// <summary>
        /// The operation being confirmed
        /// </summary>
        public required ConfirmationOperation Operation { get; set; }

        /// <summary>
        /// The field being operated on
        /// </summary>
        public required Field Field { get; set; }

        /// <summary>
        /// The item data for the operation
        /// </summary>
        public Dictionary<string, object>? ItemData { get; set; }

        /// <summary>
        /// The current task ID
        /// </summary>
        public required string TaskId { get; set; }

        /// <summary>
        /// The application reference number
        /// </summary>
        public required string ReferenceNumber { get; set; }

        /// <summary>
        /// The user's confirmation response
        /// </summary>
        public bool? UserConfirmed { get; set; }

        /// <summary>
        /// The confirmation token for security
        /// </summary>
        public required string ConfirmationToken { get; set; }

        /// <summary>
        /// The return URL after confirmation
        /// </summary>
        public string? ReturnUrl { get; set; }
    }

    /// <summary>
    /// Result of processing a confirmation
    /// </summary>
    public class ConfirmationResult
    {
        /// <summary>
        /// Whether the operation should proceed
        /// </summary>
        public required bool ShouldProceed { get; set; }

        /// <summary>
        /// The URL to redirect to after processing
        /// </summary>
        public required string RedirectUrl { get; set; }

        /// <summary>
        /// Any error message if the operation should not proceed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}
