using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using Microsoft.Extensions.Options;

namespace DfE.ExternalApplications.Web.Services;

/// <summary>
/// Implementation of internal service authentication service
/// Follows the same pattern as TestAuthenticationService but for service-level authentication
/// with added API key security
/// </summary>
public class InternalServiceAuthenticationService(
    IUserTokenServiceFactory factory,
    IUserTokenService userTokenService,
    IOptions<InternalServiceAuthOptions> config,
    ILogger<InternalServiceAuthenticationService> logger) : IInternalServiceAuthenticationService
{
    private readonly IUserTokenService _userTokenService = factory.GetService("InternalService");
    private readonly InternalServiceAuthOptions _config = config.Value;
    private readonly ILogger<InternalServiceAuthenticationService> _logger = logger;

    public bool ValidateServiceCredentials(string serviceEmail, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(serviceEmail) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogDebug("Service credentials validation failed: empty email or API key");
            return false;
        }

        // Find service configuration
        var serviceConfig = _config.Services
            .FirstOrDefault(s => s.Email.Equals(serviceEmail, StringComparison.OrdinalIgnoreCase));

        if (serviceConfig == null)
        {
            _logger.LogDebug("Service email not found in configuration: {Email}", serviceEmail);
            return false;
        }

        // Use constant-time comparison to prevent timing attacks
        var isValid = ConstantTimeEquals(serviceConfig.ApiKey, apiKey);

        if (!isValid)
        {
            _logger.LogWarning(
                "Invalid API key provided for service: {Email}", 
                serviceEmail);
        }
        else
        {
            _logger.LogDebug("Service credentials validated successfully for: {Email}", serviceEmail);
        }

        return isValid;
    }

    public async Task<string> GenerateServiceTokenAsync(string serviceEmail)
    {
        _logger.LogInformation("Generating 5-minute token for service: {Email}", serviceEmail);

        // Create claims (exactly like TestAuthenticationService)
        var claims = CreateServiceClaims(serviceEmail);
        var identity = new ClaimsIdentity(claims, "InternalServiceAuth");
        var principal = new ClaimsPrincipal(identity);

        // Generate token using UserTokenService (exactly like Test Auth)
        // The token TTL is controlled by the UserTokenService configuration
        var token = await _userTokenService.GetUserTokenAsync(principal);

        _logger.LogDebug("Token generated successfully for {Email}", serviceEmail);

        return token;
    }

    private static IEnumerable<Claim> CreateServiceClaims(string serviceEmail)
    {
        return new[]
        {
            new Claim(ClaimTypes.Email, serviceEmail),
            new Claim(ClaimTypes.NameIdentifier, serviceEmail),
            new Claim(ClaimTypes.Name, serviceEmail),
            new Claim("sub", serviceEmail),
            new Claim("email", serviceEmail),
            new Claim("service_type", "internal"),
            // Add 10-minute expiration claim
            new Claim("exp", DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString())
        };
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

