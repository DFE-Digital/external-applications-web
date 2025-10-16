﻿using System.Diagnostics.CodeAnalysis;
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

        /// <summary>
        /// Conditional logic rules for this template
        /// </summary>
        [JsonPropertyName("conditionalLogic")]
        public List<ConditionalLogic>? ConditionalLogic { get; set; }

        /// <summary>
        /// Default field requirement policy for the template.
        /// - "required": All fields are required by default unless explicitly marked as optional
        /// - "optional": All fields are optional by default unless explicitly marked as required
        /// If not specified, defaults to "optional" for backward compatibility
        /// </summary>
        [JsonPropertyName("defaultFieldRequirementPolicy")]
        public string? DefaultFieldRequirementPolicy { get; set; }
    }
}
