using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Domain.Models;

/// <summary>
/// Represents a contributor to an application
/// </summary>
[ExcludeFromCodeCoverage]
public class ContributorDto
{
    /// <summary>
    /// The unique identifier of the contributor
    /// </summary>
    [JsonPropertyName("contributorId")]
    public Guid ContributorId { get; set; }

    /// <summary>
    /// The email address of the contributor
    /// </summary>
    [JsonPropertyName("emailAddress")]
    public required string EmailAddress { get; set; }

    /// <summary>
    /// The name of the contributor
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// The status of the contributor invitation
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; set; }

    /// <summary>
    /// The date when the contributor was invited
    /// </summary>
    [JsonPropertyName("dateInvited")]
    public DateTime DateInvited { get; set; }

    /// <summary>
    /// The date when the contributor joined (if applicable)
    /// </summary>
    [JsonPropertyName("dateJoined")]
    public DateTime? DateJoined { get; set; }
} 