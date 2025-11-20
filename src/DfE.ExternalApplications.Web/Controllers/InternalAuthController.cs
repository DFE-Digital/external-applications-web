using Microsoft.AspNetCore.Mvc;
using DfE.ExternalApplications.Web.Services;

namespace DfE.ExternalApplications.Web.Controllers;

/// <summary>
/// API controller for internal service authentication token generation
/// </summary>
[ApiController]
[Route("api/internal-auth")]
public class InternalAuthController(
    IInternalServiceAuthenticationService internalAuthService,
    ILogger<InternalAuthController> logger) : ControllerBase
{
    private readonly IInternalServiceAuthenticationService _internalAuthService = internalAuthService;
    private readonly ILogger<InternalAuthController> _logger = logger;

    /// <summary>
    /// Generates a 5-minute token for an internal service
    /// Requires valid service email and API key
    /// </summary>
    /// <param name="serviceEmail">Service email from X-Service-Email header</param>
    /// <param name="apiKey">Service API key from X-Service-Api-Key header</param>
    /// <returns>A token valid for 5 minutes</returns>
    [HttpGet("get-token")]
    public async Task<IActionResult> GetToken(
        [FromHeader(Name = "X-Service-Email")] string? serviceEmail,
        [FromHeader(Name = "X-Service-Api-Key")] string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(serviceEmail))
        {
            return BadRequest(new { error = "Service email required in X-Service-Email header" });
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BadRequest(new { error = "API key required in X-Service-Api-Key header" });
        }

        // Validate credentials (email + API key)
        if (!_internalAuthService.ValidateServiceCredentials(serviceEmail, apiKey))
        {
            _logger.LogWarning(
                "Unauthorized token request: {Email} from {IP}",
                serviceEmail,
                HttpContext.Connection.RemoteIpAddress);
            
            return Unauthorized(new { error = "Invalid service credentials" });
        }

        var token = await _internalAuthService.GenerateServiceTokenAsync(serviceEmail);

        _logger.LogInformation("Issued 5-minute token for service: {Email}", serviceEmail);

        return Ok(new { token, expiresIn = 300 }); // 5 minutes
    }
}

