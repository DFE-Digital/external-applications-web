using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Domain.Models;

public class Label
{
    [JsonPropertyName("value")]
    public required string Value { get; set; }
    [JsonPropertyName("isPageHeading")]
    public bool IsPageHeading { get; set; } = false;
}