using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace DfE.ExternalApplications.Web.Services
{
    public class InternalAuthRequestChecker(
        IHostEnvironment env,
        IOptions<InternalServiceAuthOptions> config,
        ILogger<InternalAuthRequestChecker> logger)
        : ICustomRequestChecker
    {
        private const string ServiceEmailHeaderKey = "x-service-email";
        private const string ServiceApiHeaderKey = "x-service-api-key";
        private readonly InternalServiceAuthOptions _config = config.Value;

        /// <summary>
        /// Validates if the current HTTP request is a valid Cypress test request
        /// </summary>
        /// <param name="httpContext">The HTTP context to validate</param>
        /// <returns>True if this is a valid Cypress request with correct headers and secret</returns>
        public bool IsValidRequest(HttpContext httpContext)
        {
            // Check for email header
            var serviceEmail = httpContext.Request.Headers[ServiceEmailHeaderKey].ToString();
            var serviceApiKey = httpContext.Request.Headers[ServiceApiHeaderKey].ToString();

            var serviceConfig = _config.Services
                .FirstOrDefault(s => s.Email.Equals(serviceEmail, StringComparison.OrdinalIgnoreCase));

            if (serviceConfig == null)
            {
                logger.LogDebug("Service email not found in configuration: {Email}", serviceEmail);
                return false;
            }

            var isValid = ConstantTimeEquals(serviceConfig.ApiKey, serviceApiKey);

            if (!isValid)
            {
                logger.LogWarning(
                    "Invalid API key provided for service: {Email}",
                    serviceEmail);
            }
            else
            {
                logger.LogDebug("Service credentials validated successfully for: {Email}", serviceEmail);
            }


            return isValid;
        }


        /// <summary>
        /// Constant-time string comparison to prevent timing attacks
        /// </summary>
        private static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null)
                return false;

            var aBytes = Encoding.UTF8.GetBytes(a);
            var bBytes = Encoding.UTF8.GetBytes(b);

            if (aBytes.Length != bBytes.Length)
                return false;

            return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }
    }

}
