using AutoFixture;
using AutoFixture.AutoNSubstitute;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DfE.ExternalApplications.Infrastructure.UnitTests.Services;

public class FormValidationOrchestratorTests
{
    private readonly IFixture _fixture;
    private readonly FormValidationOrchestrator _orchestrator;

    public FormValidationOrchestratorTests()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization { ConfigureMembers = true });
        
        _fixture.Customize<Condition>(ob => ob.Without(rule => rule.Conditions));
        
        _orchestrator = _fixture.Create<FormValidationOrchestrator>();
    }
    
    [Theory]
    [InlineData("radios", "not-an-option")]
    [InlineData("checkboxes", "not-an-option")]
    public void ValidateField_when_required_field_with_options_and_submittedValue_is_not_in_options_then_returns_false_and_uses_required_message(string fieldType, string submittedValue)
    {
        var option1 = _fixture.Build<Option>()
            .With(o => o.Value, "option1")
            .Create();
        var option2 = _fixture.Build<Option>()
            .With(o => o.Value, "option2")
            .Create();
        var validation = _fixture.Build<ValidationRule>()
            .With(v => v.Type, "required")
            .Without(v => v.Condition)
            .With(v => v.Message, "This field is required")
            .Create();
        var field = _fixture.Build<Field>()
            .With(f => f.Type, fieldType)
            .With(f => f.Options, [option1, option2])
            .With(f => f.Validations, [validation])
            .Create();
        
        var formData = _fixture.Create<Dictionary<string, object>?>();
        var modelState = _fixture.Create<ModelStateDictionary>();
        var fieldKey = field.FieldId;
        var formTemplate = _fixture.Create<FormTemplate>();

        var result = _orchestrator.ValidateField(field, submittedValue, formData, modelState, fieldKey, formTemplate);
        
        Assert.False(result);
        Assert.NotNull(modelState[fieldKey]);
        Assert.Equal("This field is required", modelState[fieldKey]!.Errors[0].ErrorMessage);
    }
    
    [Theory]
    [InlineData("radios")]
    [InlineData("checkboxes")]
    public void ValidateField_when_optional_field_with_options_and_submittedValue_is_nonempty_and_not_in_options_then_returns_false_and_uses_fallback_message(string fieldType)
    {
        var option1 = _fixture.Build<Option>()
            .With(o => o.Value, "option1")
            .Create();
        var option2 = _fixture.Build<Option>()
            .With(o => o.Value, "option2")
            .Create();
        var field = _fixture.Build<Field>()
            .With(f => f.Type, fieldType)
            .With(f => f.Options, [option1, option2])
            .Without(f => f.Validations)
            .Create();
        
        var formData = _fixture.Create<Dictionary<string, object>?>();
        var modelState = _fixture.Create<ModelStateDictionary>();
        var fieldKey = field.FieldId;
        var formTemplate = _fixture.Create<FormTemplate>();

        var result = _orchestrator.ValidateField(field, "not-an-option", formData, modelState, fieldKey, formTemplate);
        
        Assert.False(result);
        Assert.NotNull(modelState[fieldKey]);
        Assert.Equal("Select an option from the list", modelState[fieldKey]!.Errors[0].ErrorMessage);
    }
    
    [Theory]
    [InlineData("radios", "")]
    [InlineData("radios", "    ")]
    [InlineData("checkboxes", "")]
    [InlineData("checkboxes", "    ")]
    public void ValidateField_when_optional_field_with_options_and_submittedValue_is_empty_then_returns_true(string fieldType, string submittedValue)
    {
        var option1 = _fixture.Build<Option>()
            .With(o => o.Value, "option1")
            .Create();
        var option2 = _fixture.Build<Option>()
            .With(o => o.Value, "option2")
            .Create();
        var field = _fixture.Build<Field>()
            .With(f => f.Type, fieldType)
            .With(f => f.Options, [option1, option2])
            .Without(f => f.Validations)
            .Create();
        
        var formData = _fixture.Create<Dictionary<string, object>?>();
        var modelState = _fixture.Create<ModelStateDictionary>();
        var fieldKey = field.FieldId;
        var formTemplate = _fixture.Create<FormTemplate>();

        var result = _orchestrator.ValidateField(field, submittedValue, formData, modelState, fieldKey, formTemplate);
        
        Assert.True(result);
        Assert.Null(modelState[fieldKey]);
    }
}