using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Domain.Models;

/// <summary>
/// Request model for inviting a contributor to an application
/// </summary>
[ExcludeFromCodeCoverage]
public class InviteContributorRequest
{
    /// <summary>
    /// The email address of the person to invite as a contributor
    /// </summary>
    [JsonPropertyName("emailAddress")]
    public required string EmailAddress { get; set; }

    /// <summary>
    /// The name of the person to invite as a contributor
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }
} 