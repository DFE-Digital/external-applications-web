using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Domain.Models;

public class Task
{
    [JsonPropertyName("taskId")]
    public required string TaskId { get; set; }
        
    [JsonPropertyName("taskName")]
    public required string TaskName { get; set; }

    [JsonPropertyName("taskOrder")]
    public required int TaskOrder { get; set; }

    [JsonPropertyName("taskStatus")]
    public required string TaskStatus { get; set; }

    [JsonPropertyName("pages")]
    public required List<Page> Pages { get; set; }
}