using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Web.Models
{
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

    public class TaskGroup
    {
        [JsonPropertyName("groupId")]
        public required string GroupId { get; set; }

        [JsonPropertyName("groupName")]
        public required string GroupName { get; set; }

        [JsonPropertyName("groupOrder")]
        public required int GroupOrder { get; set; }

        [JsonPropertyName("groupStatus")]
        public required string GroupStatus { get; set; }

        [JsonPropertyName("tasks")]
        public required List<Task> Tasks { get; set; }
    }

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

    public class Page
    {
        [JsonPropertyName("pageId")]
        public required string PageId { get; set; }

        [JsonPropertyName("slug")]
        public required string Slug { get; set; }

        [JsonPropertyName("title")]
        public required string Title { get; set; }

        [JsonPropertyName("description")]
        public required string Description { get; set; }

        [JsonPropertyName("pageOrder")]
        public required int PageOrder { get; set; }

        [JsonPropertyName("fields")]
        public required List<Field> Fields { get; set; }
    }

    public class Field
    {
        [JsonPropertyName("fieldId")]
        public required string FieldId { get; set; }

        [JsonPropertyName("type")]
        public required string Type { get; set; }

        [JsonPropertyName("label")]
        public required string Label { get; set; }

        [JsonPropertyName("placeholder")]
        public string? Placeholder { get; set; }

        [JsonPropertyName("tooltip")]
        public string? Tooltip { get; set; }

        [JsonPropertyName("required")]
        public bool Required { get; set; }

        [JsonPropertyName("order")]
        public required int Order { get; set; }

        [JsonPropertyName("visilbility")]
        public Visibility? Visibility { get; set; }
        [JsonPropertyName("validations")]
        public List<ValidationRule>? Validations { get; set; }

        [JsonPropertyName("options")]
        public List<Option>? Options { get; set; }

        [JsonPropertyName("complexField")]
        public string? ComplexField { get; set; }
    }

    public class Option
    {
        [JsonPropertyName("value")]
        public required string Value { get; set; }

        [JsonPropertyName("label")]
        public required string Label { get; set; }
    }

    public class Visibility
    {
        [JsonPropertyName("default")]
        public bool Default { get; set; }
    }

    public class ValidationRule
    {
        [JsonPropertyName("type")]
        public required string Type { get; set; }            // "required", "regex", "maxLength"

        [JsonPropertyName("rule")]
        public required object Rule { get; set; }            // pattern or numeric limit

        [JsonPropertyName("message")]
        public required string Message { get; set; }

        //[JsonPropertyName("condition")]
        //public Condition? Condition { get; set; }    // optional conditional application
    }

    //public class ConditionalLogic
    //{
    //    [JsonPropertyName("conditionGroup")]
    //    public required ConditionGroup ConditionGroup { get; set; }

    //    [JsonPropertyName("affectedElements")]
    //    public required List<string> AffectedElements { get; set; }

    //    [JsonPropertyName("action")]
    //    public required string Action { get; set; }
    //}

    //public class ConditionGroup
    //{
    //    [JsonPropertyName("logicalOperator")]
    //    public required string LogicalOperator { get; set; }

    //    [JsonPropertyName("conditions")]
    //    public required List<Condition> Conditions { get; set; }
    //}

    //public class Condition
    //{
    //    [JsonPropertyName("triggerField")]
    //    public required string TriggerField { get; set; }

    //    [JsonPropertyName("operator")]
    //    public required string Operator { get; set; }
        
    //    [JsonPropertyName("value")]
    //    public required object Value { get; set; }
    //}
}
