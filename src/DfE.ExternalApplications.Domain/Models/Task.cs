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
}