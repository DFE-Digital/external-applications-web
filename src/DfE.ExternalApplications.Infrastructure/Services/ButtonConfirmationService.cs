using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;

namespace DfE.ExternalApplications.Infrastructure.Services
{
    /// <summary>
    /// Implementation of the button confirmation service
    /// </summary>
    public class ButtonConfirmationService : IButtonConfirmationService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfirmationDataService _dataService;
        private readonly ILogger<ButtonConfirmationService> _logger;

        public ButtonConfirmationService(
            IHttpContextAccessor httpContextAccessor,
            IConfirmationDataService dataService,
            ILogger<ButtonConfirmationService> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _dataService = dataService;
            _logger = logger;
        }

        /// <summary>
        /// Creates a confirmation request and returns a token
        /// </summary>
        /// <param name="request">The confirmation request details</param>
        /// <returns>A unique confirmation token</returns>
        public string CreateConfirmation(ConfirmationRequest request)
        {
            var token = GenerateSecureToken();
            request.ConfirmationToken = token;

            var context = new ConfirmationContext
            {
                Token = token,
                Request = request,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10) // 10 minute expiry
            };

            // Store in session
            var session = _httpContextAccessor.HttpContext?.Session;
            if (session != null)
            {
                var key = $"Confirmation_{token}";
                var serializedContext = JsonSerializer.Serialize(context);
                session.SetString(key, serializedContext);

                _logger.LogInformation("Created confirmation with token {Token} for handler {Handler}",
                    token, request.OriginalHandler);
            }
            else
            {
                _logger.LogError("Unable to create confirmation - HttpContext or Session is null");
                throw new InvalidOperationException("Session is not available");
            }

            return token;
        }

        /// <summary>
        /// Retrieves a confirmation context by token
        /// </summary>
        /// <param name="token">The confirmation token</param>
        /// <returns>The confirmation context or null if not found/expired</returns>
        public ConfirmationContext? GetConfirmation(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;

            var session = _httpContextAccessor.HttpContext?.Session;
            if (session == null)
                return null;

            var key = $"Confirmation_{token}";
            var data = session.GetString(key);

            if (string.IsNullOrEmpty(data))
            {
                _logger.LogWarning("Confirmation token {Token} not found in session", token);
                return null;
            }

            try
            {
                var context = JsonSerializer.Deserialize<ConfirmationContext>(data);
                if (context?.IsExpired == true)
                {
                    _logger.LogWarning("Confirmation token {Token} has expired", token);
                    session.Remove(key);
                    return null;
                }

                return context;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize confirmation context for token {Token}", token);
                session.Remove(key);
                return null;
            }
        }

        /// <summary>
        /// Prepares the display model for the confirmation page
        /// </summary>
        /// <param name="token">The confirmation token</param>
        /// <returns>The display model or null if token is invalid</returns>
        public ConfirmationDisplayModel? PrepareDisplayModel(string token)
        {
            var context = GetConfirmation(token);
            if (context == null)
                return null;

            // Normalize form data to primitive types so the confirmation view can re-post it
            var normalizedFormData = NormalizeFormData(context.Request.OriginalFormData);

            var displayData = _dataService.FormatDisplayData(
                normalizedFormData,
                context.Request.DisplayFields);

            return new ConfirmationDisplayModel
            {
                Title = "Confirm your action",
                Message = "Please review the following information:",
                DisplayData = displayData,
                ReturnUrl = context.Request.ReturnUrl,
                ConfirmationToken = token,
                OriginalActionUrl = $"{context.Request.OriginalPagePath}?handler={context.Request.OriginalHandler}",
                OriginalFormData = normalizedFormData
            };
        }

        /// <summary>
        /// Converts deserialized form data (which may contain JsonElement) into strings or string arrays
        /// suitable for reposting from the confirmation page.
        /// </summary>
        /// <param name="formData">Original form data from the intercepted request</param>
        /// <returns>Dictionary with values as string or string[]</returns>
        private static Dictionary<string, object> NormalizeFormData(Dictionary<string, object> formData)
        {
            var result = new Dictionary<string, object>();

            if (formData == null || formData.Count == 0)
            {
                return result;
            }

            // Exclude antiforgery field; the confirmation page emits its own token
            var keysToSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "__RequestVerificationToken"
            };

            foreach (var kvp in formData)
            {
                var key = kvp.Key;
                var value = kvp.Value;

                if (keysToSkip.Contains(key))
                {
                    continue;
                }

                switch (value)
                {
                    case string s:
                        result[key] = s;
                        break;
                    case string[] arr:
                        result[key] = arr;
                        break;
                    case JsonElement je:
                        result[key] = ConvertJsonElement(je);
                        break;
                    default:
                        result[key] = value?.ToString() ?? string.Empty;
                        break;
                }
            }

            return result;
        }

        private static object ConvertJsonElement(JsonElement je)
        {
            switch (je.ValueKind)
            {
                case JsonValueKind.String:
                    return je.GetString() ?? string.Empty;
                case JsonValueKind.Number:
                    return je.ToString();
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return je.GetBoolean().ToString();
                case JsonValueKind.Array:
                {
                    var list = new List<string>();
                    foreach (var item in je.EnumerateArray())
                    {
                        list.Add(item.ToString());
                    }
                    return list.ToArray();
                }
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return string.Empty;
                default:
                    return je.ToString();
            }
        }

        /// <summary>
        /// Clears an expired or used confirmation from storage
        /// </summary>
        /// <param name="token">The confirmation token to clear</param>
        public void ClearConfirmation(string token)
        {
            if (string.IsNullOrEmpty(token))
                return;

            var session = _httpContextAccessor.HttpContext?.Session;
            if (session != null)
            {
                var key = $"Confirmation_{token}";
                session.Remove(key);
                _logger.LogInformation("Cleared confirmation token {Token}", token);
            }
        }

        /// <summary>
        /// Validates that a confirmation token is valid and not expired
        /// </summary>
        /// <param name="token">The confirmation token</param>
        /// <returns>True if the token is valid and not expired</returns>
        public bool IsValidToken(string token)
        {
            var context = GetConfirmation(token);
            return context != null && !context.IsExpired;
        }

        /// <summary>
        /// Generates a cryptographically secure token
        /// </summary>
        /// <returns>A secure token string</returns>
        private static string GenerateSecureToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }
    }
}
