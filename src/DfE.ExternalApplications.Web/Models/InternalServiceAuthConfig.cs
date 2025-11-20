namespace DfE.ExternalApplications.Web.Models;

/// <summary>
/// Configuration for internal service authentication
/// </summary>
public class InternalServiceAuthConfig
{
    /// <summary>
    /// List of authorized internal services with their credentials
    /// </summary>
    public List<InternalServiceCredentials> Services { get; set; } = new();
}

/// <summary>
/// Credentials for a specific internal service
/// </summary>
public class InternalServiceCredentials
{
    /// <summary>
    /// Service email identifier
    /// </summary>
    public string Email { get; set; } = string.Empty;
    
    /// <summary>
    /// Shared API key for authentication (minimum 32 characters recommended)
    /// Should be stored in Azure Key Vault in production
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}


