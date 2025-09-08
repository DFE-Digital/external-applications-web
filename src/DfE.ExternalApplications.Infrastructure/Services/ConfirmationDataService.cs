using DfE.ExternalApplications.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace DfE.ExternalApplications.Infrastructure.Services
{
    /// <summary>
    /// Implementation of the confirmation data service for formatting display data
    /// </summary>
    public class ConfirmationDataService : IConfirmationDataService
    {
        private readonly ILogger<ConfirmationDataService> _logger;

        public ConfirmationDataService(ILogger<ConfirmationDataService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Formats form data for display on the confirmation page
        /// </summary>
        /// <param name="formData">The raw form data</param>
        /// <param name="displayFields">The fields to include in the display</param>
        /// <returns>A dictionary of display-friendly field names and values</returns>
        public Dictionary<string, string> FormatDisplayData(Dictionary<string, object> formData, string[] displayFields)
        {
            var result = new Dictionary<string, string>();

            if (formData == null || !formData.Any())
            {
                _logger.LogWarning("No form data provided for confirmation display");
                return result;
            }

            // If no specific display fields are specified, show all non-system fields
            var fieldsToShow = displayFields?.Any() == true 
                ? displayFields 
                : formData.Keys.Where(k => !IsSystemField(k)).ToArray();

            foreach (var fieldName in fieldsToShow)
            {
                if (formData.TryGetValue(fieldName, out var value))
                {
                    var displayName = GetFieldDisplayName(fieldName);
                    var formattedValue = FormatFieldValue(fieldName, value);
                    
                    if (!string.IsNullOrWhiteSpace(formattedValue))
                    {
                        result[displayName] = formattedValue;
                    }
                }
                else
                {
                    _logger.LogWarning("Display field {FieldName} not found in form data", fieldName);
                }
            }

            _logger.LogInformation("Formatted {Count} fields for confirmation display", result.Count);
            return result;
        }

        /// <summary>
        /// Gets a user-friendly display name for a field
        /// </summary>
        /// <param name="fieldName">The field name</param>
        /// <returns>A user-friendly display name</returns>
        public string GetFieldDisplayName(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
                return string.Empty;

            // Handle common field mappings
            var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "trustName", "Trust Name" },
                { "ukprn", "UKPRN" },
                { "urn", "URN" },
                { "companiesHouseNumber", "Companies House Number" },
                { "contributorEmail", "Email Address" },
                { "contributorName", "Full Name" },
                { "contributorId", "Contributor ID" },
                { "firstName", "First Name" },
                { "lastName", "Last Name" },
                { "emailAddress", "Email Address" },
                { "phoneNumber", "Phone Number" },
                { "postcode", "Postcode" },
                { "addressLine1", "Address Line 1" },
                { "addressLine2", "Address Line 2" },
                { "city", "City" },
                { "county", "County" },
                { "country", "Country" }
            };

            if (mappings.TryGetValue(fieldName, out var displayName))
                return displayName;

            // Convert camelCase/PascalCase to Title Case
            return ConvertToTitleCase(fieldName);
        }

        /// <summary>
        /// Formats a field value for display
        /// </summary>
        /// <param name="fieldName">The field name</param>
        /// <param name="value">The field value</param>
        /// <returns>A formatted display value</returns>
        public string FormatFieldValue(string fieldName, object value)
        {
            if (value == null)
                return string.Empty;

            var stringValue = value.ToString();
            if (string.IsNullOrWhiteSpace(stringValue))
                return string.Empty;

            // Handle specific field formatting
            switch (fieldName.ToLowerInvariant())
            {
                case "ukprn":
                    return FormatUkprn(stringValue);
                
                case "companieshhousenumber":
                case "companieshousenumber":
                    return FormatCompaniesHouseNumber(stringValue);
                
                case "postcode":
                    return FormatPostcode(stringValue);
                
                case "emailaddress":
                case "contributoremail":
                case "email":
                    return FormatEmail(stringValue);
                
                case "phonenumber":
                case "phone":
                    return FormatPhoneNumber(stringValue);
                
                default:
                    return stringValue.Trim();
            }
        }

        /// <summary>
        /// Determines if a field is a system field that should not be displayed by default
        /// </summary>
        /// <param name="fieldName">The field name</param>
        /// <returns>True if it's a system field</returns>
        private static bool IsSystemField(string fieldName)
        {
            var systemFields = new[]
            {
                "__RequestVerificationToken",
                "handler",
                "CurrentPageId",
                "TaskId",
                "FlowId",
                "InstanceId"
            };

            return systemFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Converts camelCase/PascalCase to Title Case
        /// </summary>
        /// <param name="input">The input string</param>
        /// <returns>Title case string</returns>
        private static string ConvertToTitleCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // Add spaces before capital letters
            var result = string.Empty;
            for (int i = 0; i < input.Length; i++)
            {
                if (i > 0 && char.IsUpper(input[i]) && char.IsLower(input[i - 1]))
                {
                    result += " ";
                }
                result += input[i];
            }

            // Convert to title case
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(result.ToLower());
        }

        /// <summary>
        /// Formats a UKPRN value
        /// </summary>
        /// <param name="value">The UKPRN value</param>
        /// <returns>Formatted UKPRN</returns>
        private static string FormatUkprn(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            // UKPRN should be 8 digits
            if (value.Length == 8 && value.All(char.IsDigit))
                return value;

            return value.Trim();
        }

        /// <summary>
        /// Formats a Companies House number
        /// </summary>
        /// <param name="value">The Companies House number</param>
        /// <returns>Formatted Companies House number</returns>
        private static string FormatCompaniesHouseNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim().ToUpperInvariant();
        }

        /// <summary>
        /// Formats a postcode
        /// </summary>
        /// <param name="value">The postcode</param>
        /// <returns>Formatted postcode</returns>
        private static string FormatPostcode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim().ToUpperInvariant();
        }

        /// <summary>
        /// Formats an email address
        /// </summary>
        /// <param name="value">The email address</param>
        /// <returns>Formatted email address</returns>
        private static string FormatEmail(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Formats a phone number
        /// </summary>
        /// <param name="value">The phone number</param>
        /// <returns>Formatted phone number</returns>
        private static string FormatPhoneNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim();
        }
    }
}

