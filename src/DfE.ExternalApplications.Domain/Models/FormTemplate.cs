using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Domain.Models
{
    [ExcludeFromCodeCoverage]
    public class FormTemplate
    {
        [JsonPropertyName("templateId")]
        public required string TemplateId { get; set; }

        [JsonPropertyName("templateName")]
        public required string TemplateName { get; set; }

        [JsonPropertyName("description")]
        public required string Description { get; set; }

            [JsonPropertyName("taskGroups")]
    public required List<TaskGroup> TaskGroups { get; set; }

        //[JsonPropertyName("conditionalLogic")]
        //public List<ConditionalLogic>? ConditionalLogic { get; set; }
    }
}
