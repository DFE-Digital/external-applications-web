using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Domain.Models;

[ExcludeFromCodeCoverage]
public class Task
{
    [JsonPropertyName("taskId")]
    public required string TaskId { get; set; }
        
    [JsonPropertyName("taskName")]
    public required string TaskName { get; set; }

    [JsonPropertyName("taskOrder")]
    public required int TaskOrder { get; set; }

    [JsonPropertyName("taskStatus")]
    public required string TaskStatusString { get; set; }

    [JsonIgnore]
    public TaskStatus TaskStatus 
    { 
        get => Enum.TryParse<TaskStatus>(TaskStatusString, out var result) ? result : TaskStatus.NotStarted;
        set => TaskStatusString = value.ToString();
    }

    [JsonPropertyName("pages")]
    public required List<Page> Pages { get; set; }

    // Custom summary configuration (optional)
    [JsonPropertyName("summary")] public TaskSummaryConfiguration? Summary { get; set; }

    // Control visibility in main task list
    [JsonPropertyName("visibleInTaskList")] public bool? VisibleInTaskList { get; set; }
}

public class TaskSummaryConfiguration
{
    // "standard" or "collectionFlow"
    [JsonPropertyName("mode")] public string Mode { get; set; } = "standard";

    // For collectionFlow mode
    [JsonPropertyName("fieldId")] public string? FieldId { get; set; }
    [JsonPropertyName("addButtonLabel")] public string? AddButtonLabel { get; set; }
    [JsonPropertyName("minItems")] public int? MinItems { get; set; }
    [JsonPropertyName("maxItems")] public int? MaxItems { get; set; }

    // UI helpers for collection flow summaries (moved from FlowDefinition)
    [JsonPropertyName("itemTitleBinding")] public string? ItemTitleBinding { get; set; }
    [JsonPropertyName("summaryColumns")] public List<FlowSummaryColumn>? SummaryColumns { get; set; }
}

/// <summary>
/// Represents a column in the collection flow summary display
/// </summary>
public class FlowSummaryColumn
{
    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
    [JsonPropertyName("field")] public string Field { get; set; } = string.Empty;
}