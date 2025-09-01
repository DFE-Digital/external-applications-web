using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DfE.ExternalApplications.Infrastructure.Services
{
    /// <summary>
    /// Implementation of the confirmation service that handles confirmation logic for add/update/delete operations
    /// </summary>
    public class ConfirmationService : IConfirmationService
    {
        private readonly ILogger<ConfirmationService> _logger;

        public ConfirmationService(ILogger<ConfirmationService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Determines if a field requires confirmation for the specified operation
        /// </summary>
        /// <param name="field">The field to check</param>
        /// <param name="operation">The operation being performed (add, update, delete)</param>
        /// <returns>True if confirmation is required</returns>
        public bool RequiresConfirmation(Field field, ConfirmationOperation operation)
        {
            // Add logging to debug confirmation logic
            _logger.LogInformation("RequiresConfirmation called for field: {FieldId}, Type: {FieldType}, Operation: {Operation}", 
                field.FieldId, field.Type, operation);
            _logger.LogInformation("Field has ComplexField: {HasComplexField}", field.ComplexField != null);
            _logger.LogInformation("Field Visibility RequireConfirmation: {RequireConfirmation}", field.Visibility?.RequireConfirmation);
            
            // Check if the field has a confirmation requirement
            if (field.Visibility?.RequireConfirmation == true)
            {
                _logger.LogInformation("Field {FieldId} requires confirmation due to Visibility.RequireConfirmation", field.FieldId);
                return true;
            }

            // Check if the field type requires confirmation for certain operations
            if (field.Type == "complexField" && field.ComplexField != null)
            {
                _logger.LogInformation("Field {FieldId} is a complex field with ID: {ComplexFieldId}", 
                    field.FieldId, field.ComplexField.Id);
                
                // Complex fields like file uploads might require confirmation for delete operations
                if (operation == ConfirmationOperation.Delete)
                {
                    _logger.LogInformation("Field {FieldId} requires confirmation for delete operation", field.FieldId);
                    return true;
                }
                
                // For complex fields, check if they require confirmation based on their configuration
                if (operation == ConfirmationOperation.Update)
                {
                    // Trust selection fields should require confirmation for updates
                    if (field.ComplexField.Id.Equals("TrustComplexField", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Field {FieldId} requires confirmation for update operation (trust selection)", field.FieldId);
                        return true;
                    }
                    
                    // File upload fields should require confirmation for updates
                    if (field.ComplexField.Id.Equals("UploadDocumentsComplexField", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Field {FieldId} requires confirmation for update operation (file upload)", field.FieldId);
                        return true;
                    }
                }
            }

            // Check for autocomplete fields specifically
            if (field.Type == "autocomplete")
            {
                _logger.LogInformation("Field {FieldId} is an autocomplete field", field.FieldId);
                // Autocomplete fields might require confirmation for update operations
                if (operation == ConfirmationOperation.Update)
                {
                    _logger.LogInformation("Field {FieldId} requires confirmation for update operation (autocomplete)", field.FieldId);
                    return true;
                }
            }

            // Check if the field has validation rules that suggest confirmation is needed
            if (field.Validations?.Any(v => v.Type == "confirmation") == true)
            {
                _logger.LogInformation("Field {FieldId} requires confirmation due to validation rules", field.FieldId);
                return true;
            }

            _logger.LogInformation("Field {FieldId} does NOT require confirmation", field.FieldId);
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
