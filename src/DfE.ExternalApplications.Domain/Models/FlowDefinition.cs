using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Domain.Models;

public class FlowDefinition
{
    [JsonPropertyName("flowId")] public required string FlowId { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("pages")] public required List<Page> Pages { get; set; }

    // UI helpers for list summaries
    [JsonPropertyName("itemTitleBinding")] public string? ItemTitleBinding { get; set; }

    [JsonPropertyName("summaryColumns")] public List<FlowSummaryColumn>? SummaryColumns { get; set; }
}

public class FlowSummaryColumn
{
    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
    [JsonPropertyName("field")] public string Field { get; set; } = string.Empty;
}


