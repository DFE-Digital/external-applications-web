using GovUK.Dfe.CoreLibs.Security.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Web.Services;

/// <summary>
/// Custom request checker for External Applications that validates Cypress test requests
/// using X-Cypress-Test and X-Cypress-Secret headers
/// </summary>
[ExcludeFromCodeCoverage]
public class ExternalAppsCypressRequestChecker(
    IHostEnvironment env,
    IConfiguration config,
    ILogger<ExternalAppsCypressRequestChecker> logger)
    : ICustomRequestChecker
{
    private const string CypressHeaderKey = "x-cypress-test";
    private const string CypressSecretHeaderKey = "x-cypress-secret";
    private const string ExpectedCypressValue = "true";

    /// <summary>
    /// Validates if the current HTTP request is a valid Cypress test request
    /// </summary>
    /// <param name="httpContext">The HTTP context to validate</param>
    /// <returns>True if this is a valid Cypress request with correct headers and secret</returns>
    public bool IsValidRequest(HttpContext httpContext)
    {
        // Check for Cypress header
        var cypressHeader = httpContext.Request.Headers[CypressHeaderKey].ToString();
        if (!string.Equals(cypressHeader, ExpectedCypressValue, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Only allow in Development, Staging or Test environments (NOT Production)
        if (!(env.IsDevelopment() || env.IsStaging() || env.IsEnvironment("Test")))
        {
            logger.LogWarning(
                "Cypress authentication attempted in {Environment} environment from {IP} - rejected",
                env.EnvironmentName,
                httpContext.Connection.RemoteIpAddress);
            return false;
        }

        // Check if Cypress toggle is allowed in configuration
        var allowCypressToggle = config.GetValue<bool>("CypressAuthentication:AllowToggle");
        if (!allowCypressToggle)
        {
            logger.LogWarning(
                "Cypress authentication attempted but AllowToggle is disabled from {IP}",
                httpContext.Connection.RemoteIpAddress);
            return false;
        }

        // Validate secret
        var expectedSecret = config["CypressAuthentication:Secret"];
        var providedSecret = httpContext.Request.Headers[CypressSecretHeaderKey].ToString();

        if (string.IsNullOrWhiteSpace(expectedSecret) || string.IsNullOrWhiteSpace(providedSecret))
        {
            logger.LogWarning(
                "Cypress authentication attempted with missing secret from {IP}",
                httpContext.Connection.RemoteIpAddress);
            return false;
        }

        var isValid = string.Equals(providedSecret, expectedSecret, StringComparison.Ordinal);

        if (isValid)
        {
            logger.LogInformation(
                "Valid Cypress test request detected from {IP} for path {Path}",
                httpContext.Connection.RemoteIpAddress,
                httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(
                "Invalid Cypress secret provided from {IP} for path {Path}",
                httpContext.Connection.RemoteIpAddress,
                httpContext.Request.Path);
        }

        return isValid;
    }
}

