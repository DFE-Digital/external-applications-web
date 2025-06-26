using System.Text.Json.Serialization;
using DfE.ExternalApplications.Domain.Models;

namespace DfE.ExternalApplications.Domain.Models;

public class ValidationRule
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }            // "required", "regex", "maxLength"

    [JsonPropertyName("rule")]
    public required object Rule { get; set; }            // pattern or numeric limit

    [JsonPropertyName("message")]
    public required string Message { get; set; }

    [JsonPropertyName("condition")]
    public Condition? Condition { get; set; }    // optional conditional application
}