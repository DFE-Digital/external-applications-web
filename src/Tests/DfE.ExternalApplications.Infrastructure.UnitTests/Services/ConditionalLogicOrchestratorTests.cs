using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Infrastructure.UnitTests.Services;

public class ConditionalLogicOrchestratorTests
{
    private readonly ConditionalLogicOrchestrator _orchestrator = new(
        new ConditionalLogicEngine(NullLogger<ConditionalLogicEngine>.Instance),
        NullLogger<ConditionalLogicOrchestrator>.Instance);

    [Fact]
    public async Task ApplyConditionalLogicAsync_when_parent_hidden_ignores_stale_child_value_and_hides_grandchild()
    {
        // A shows B; B shows C. After A no longer matches, B is hidden but still has a stored answer.
        // That stale B answer must not keep C visible.
        var template = CreateAbcTemplate();
        var formData = new Dictionary<string, object>
        {
            ["fieldA"] = "no",
            ["fieldB"] = "yes",
            ["fieldC"] = ""
        };

        var state = await _orchestrator.ApplyConditionalLogicAsync(template, formData);

        Assert.False(state.FieldVisibility["fieldB"]);
        Assert.False(state.FieldVisibility["fieldC"]);
    }

    [Fact]
    public async Task ApplyConditionalLogicAsync_when_chain_conditions_met_shows_all_dependent_fields()
    {
        var template = CreateAbcTemplate();
        var formData = new Dictionary<string, object>
        {
            ["fieldA"] = "yes",
            ["fieldB"] = "yes",
            ["fieldC"] = ""
        };

        var state = await _orchestrator.ApplyConditionalLogicAsync(template, formData);

        Assert.True(state.FieldVisibility["fieldA"]);
        Assert.True(state.FieldVisibility["fieldB"]);
        Assert.True(state.FieldVisibility["fieldC"]);
    }

    [Fact]
    public async Task ApplyConditionalLogicAsync_does_not_mutate_original_form_data_when_suppressing_hidden_values()
    {
        var template = CreateAbcTemplate();
        var formData = new Dictionary<string, object>
        {
            ["fieldA"] = "no",
            ["fieldB"] = "yes"
        };

        await _orchestrator.ApplyConditionalLogicAsync(template, formData);

        Assert.Equal("yes", formData["fieldB"]);
    }

    private static FormTemplate CreateAbcTemplate()
    {
        var fieldA = CreateField("fieldA", 1, required: true);
        var fieldB = CreateField("fieldB", 2, required: true);
        var fieldC = CreateField("fieldC", 3, required: true);

        return new FormTemplate
        {
            TemplateId = "test",
            TemplateName = "test",
            Description = "test",
            DefaultFieldRequirementPolicy = "optional",
            TaskGroups =
            [
                new TaskGroup
                {
                    GroupId = "g1",
                    GroupName = "Group",
                    GroupOrder = 1,
                    GroupStatus = "NotStarted",
                    Tasks =
                    [
                        new Domain.Models.Task
                        {
                            TaskId = "t1",
                            TaskName = "Task",
                            TaskOrder = 1,
                            TaskStatusString = "NotStarted",
                            Pages =
                            [
                                new Page
                                {
                                    PageId = "p1",
                                    Slug = "page-1",
                                    Title = "Page",
                                    Description = "Page",
                                    PageOrder = 1,
                                    Fields = [fieldA, fieldB, fieldC]
                                }
                            ]
                        }
                    ]
                }
            ],
            ConditionalLogic =
            [
                new ConditionalLogic
                {
                    Id = "show-b-when-a-yes",
                    Priority = 1,
                    Enabled = true,
                    ConditionGroup = new ConditionGroup
                    {
                        LogicalOperator = ConditionalLogicConstants.LogicalOperators.And,
                        Conditions =
                        [
                            new Condition
                            {
                                TriggerField = "fieldA",
                                Operator = ConditionalLogicConstants.Operators.Equals,
                                Value = "yes"
                            }
                        ]
                    },
                    AffectedElements =
                    [
                        new AffectedElement
                        {
                            ElementId = "fieldB",
                            ElementType = ConditionalLogicConstants.ElementTypes.Field,
                            Action = ConditionalLogicConstants.Actions.Show
                        }
                    ]
                },
                new ConditionalLogic
                {
                    Id = "show-c-when-b-yes",
                    Priority = 2,
                    Enabled = true,
                    ConditionGroup = new ConditionGroup
                    {
                        LogicalOperator = ConditionalLogicConstants.LogicalOperators.And,
                        Conditions =
                        [
                            new Condition
                            {
                                TriggerField = "fieldB",
                                Operator = ConditionalLogicConstants.Operators.Equals,
                                Value = "yes"
                            }
                        ]
                    },
                    AffectedElements =
                    [
                        new AffectedElement
                        {
                            ElementId = "fieldC",
                            ElementType = ConditionalLogicConstants.ElementTypes.Field,
                            Action = ConditionalLogicConstants.Actions.Show
                        }
                    ]
                }
            ]
        };
    }

    private static Field CreateField(string fieldId, int order, bool required) =>
        new()
        {
            FieldId = fieldId,
            Type = "radios",
            Order = order,
            Required = required,
            Label = new Label { Value = fieldId }
        };
}
