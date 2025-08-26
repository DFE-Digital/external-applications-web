using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using System.Security.Cryptography;
using System.Text;

namespace DfE.ExternalApplications.Infrastructure.Services
{
    /// <summary>
    /// Implementation of the confirmation service that handles confirmation logic for add/update/delete operations
    /// </summary>
    public class ConfirmationService : IConfirmationService
    {
        /// <summary>
        /// Determines if a field requires confirmation before proceeding
        /// </summary>
        /// <param name="field">The field to check</param>
        /// <param name="operation">The operation being performed (add, update, delete)</param>
        /// <returns>True if confirmation is required</returns>
        public bool RequiresConfirmation(Field field, ConfirmationOperation operation)
        {
            // Check if the field has a confirmation requirement
            if (field.Visibility?.RequireConfirmation == true)
            {
                return true;
            }

            // Check if the field type requires confirmation for certain operations
            if (field.Type == "complexField" && field.ComplexField != null)
            {
                // Complex fields like file uploads might require confirmation for delete operations
                if (operation == ConfirmationOperation.Delete)
                {
                    return true;
                }
            }

            // Check for autocomplete fields specifically
            if (field.Type == "autocomplete")
            {
                // Autocomplete fields might require confirmation for update operations
                if (operation == ConfirmationOperation.Update)
                {
                    return true;
                }
            }

            // Check if the field has validation rules that suggest confirmation is needed
            if (field.Validations?.Any(v => v.Type == "confirmation") == true)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a confirmation model for the specified operation
        /// </summary>
        /// <param name="field">The field being operated on</param>
        /// <param name="operation">The operation being performed</param>
        /// <param name="itemData">The item data for the operation</param>
        /// <param name="taskId">The current task ID</param>
        /// <param name="referenceNumber">The application reference number</param>
        /// <returns>A confirmation model</returns>
        public ConfirmationModel CreateConfirmationModel(Field field, ConfirmationOperation operation, Dictionary<string, object>? itemData, string taskId, string referenceNumber)
        {
            var confirmationToken = GenerateConfirmationToken();
            
            return new ConfirmationModel
            {
                Operation = operation,
                Field = field,
                ItemData = itemData,
                TaskId = taskId,
                ReferenceNumber = referenceNumber,
                ConfirmationToken = confirmationToken,
                ReturnUrl = GenerateReturnUrl(taskId, referenceNumber)
            };
        }

        /// <summary>
        /// Processes the confirmation result and returns the next action
        /// </summary>
        /// <param name="confirmationModel">The confirmation model with user's response</param>
        /// <returns>The next action to take</returns>
        public ConfirmationResult ProcessConfirmation(ConfirmationModel confirmationModel)
        {
            if (!confirmationModel.UserConfirmed.HasValue)
            {
                return new ConfirmationResult
                {
                    ShouldProceed = false,
                    RedirectUrl = confirmationModel.ReturnUrl ?? $"/applications/{confirmationModel.ReferenceNumber}/{confirmationModel.TaskId}",
                    ErrorMessage = "Confirmation response is required"
                };
            }

            if (confirmationModel.UserConfirmed.Value)
            {
                // User confirmed, proceed with the operation
                return new ConfirmationResult
                {
                    ShouldProceed = true,
                    RedirectUrl = confirmationModel.ReturnUrl ?? $"/applications/{confirmationModel.ReferenceNumber}/{confirmationModel.TaskId}",
                    ErrorMessage = null
                };
            }
            else
            {
                // User declined, return to the previous page
                return new ConfirmationResult
                {
                    ShouldProceed = false,
                    RedirectUrl = confirmationModel.ReturnUrl ?? $"/applications/{confirmationModel.ReferenceNumber}/{confirmationModel.TaskId}",
                    ErrorMessage = null
                };
            }
        }

        /// <summary>
        /// Generates a secure confirmation token
        /// </summary>
        /// <returns>A secure confirmation token</returns>
        private static string GenerateConfirmationToken()
        {
            var randomBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            return Convert.ToBase64String(randomBytes);
        }

        /// <summary>
        /// Generates the return URL for after confirmation
        /// </summary>
        /// <param name="taskId">The current task ID</param>
        /// <param name="referenceNumber">The application reference number</param>
        /// <returns>The return URL</returns>
        private static string GenerateReturnUrl(string taskId, string referenceNumber)
        {
            return $"/applications/{referenceNumber}/{taskId}";
        }
    }
}
