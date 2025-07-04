using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Domain.Models;

[ExcludeFromCodeCoverage]
public class Visibility
{
    [JsonPropertyName("default")]
    public bool Default { get; set; }
}