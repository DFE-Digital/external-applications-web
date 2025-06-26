using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Domain.Models;

public class ConditionGroup
{
    [JsonPropertyName("logicalOperator")]
    public required string LogicalOperator { get; set; }

    [JsonPropertyName("conditions")]
    public required List<Condition> Conditions { get; set; }
}