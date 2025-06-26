using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Domain.Models;

public class Visibility
{
    [JsonPropertyName("default")]
    public bool Default { get; set; }
}