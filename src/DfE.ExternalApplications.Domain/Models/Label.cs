using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Domain.Models;

[ExcludeFromCodeCoverage]
public class Label
{
    [JsonPropertyName("value")]
    public required string Value { get; set; }
    [JsonPropertyName("isVisible")]
    public bool IsVisible { get; set; } = false;
    [JsonPropertyName("validationLabelValue")]
    public string? ValidationLabelValue { get; set; }
}