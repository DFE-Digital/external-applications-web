using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Domain.Models;

public class Option
{
    [JsonPropertyName("value")]
    public required string Value { get; set; }

    [JsonPropertyName("label")]
    public required string Label { get; set; }
}