using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Domain.Models;

[ExcludeFromCodeCoverage]
public class ConditionalLogic
{
    [JsonPropertyName("conditionGroup")]
    public required ConditionGroup ConditionGroup { get; set; }

    [JsonPropertyName("affectedElements")]
    public required List<string> AffectedElements { get; set; }

    [JsonPropertyName("action")]
    public required string Action { get; set; }
}