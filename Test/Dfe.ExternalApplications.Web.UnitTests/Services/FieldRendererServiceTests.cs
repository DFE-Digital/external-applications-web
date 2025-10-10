using AutoFixture;
using AutoFixture.AutoNSubstitute;
using AutoFixture.Kernel;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Services;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;
using Task = System.Threading.Tasks.Task;

namespace Dfe.ExternalApplications.Web.UnitTests.Services;

public class FieldRendererServiceTests
{
    private readonly IFixture _fixture;
    private readonly IServiceProvider _serviceProvider;
    private readonly FieldRendererService _service;

    public FieldRendererServiceTests()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization { ConfigureMembers = true });

        _fixture.Customize<ValidationRule>(ob => ob.Without(rule => rule.Condition));
        _fixture.Customize<ActionDescriptor>(ob =>
            ob.Without(desc => desc.Parameters).Without(desc => desc.BoundProperties));

        _serviceProvider = Substitute.For<IServiceProvider>();
        _serviceProvider.GetService(Arg.Any<Type>())
            .Returns(call => _fixture.Create(call.Arg<Type>(), new SpecimenContext(_fixture)));

        _fixture.Register(() => _serviceProvider);

        _service = _fixture.Create<FieldRendererService>();
    }

    [Fact]
    public async Task RenderFieldAsync_returns_expected_model()
    {
        var htmlHelper = Substitute.For([typeof(IHtmlHelper), typeof(IViewContextAware)], []) as IHtmlHelper;

        object? capturedModel = null;
        htmlHelper!.PartialAsync(Arg.Any<string>(), Arg.Do<object?>(obj => capturedModel = obj), null)
            .Returns(_fixture.Create<IHtmlContent>());

        _serviceProvider.GetService(typeof(IHtmlHelper)).Returns(htmlHelper);

        var field = _fixture.Build<Field>().With(f => f.Type, "text").Create();
        var prefix = _fixture.Create<string>();
        var currentValue = _fixture.Create<string>();
        var errorMessage = _fixture.Create<string>();

        var result = await _service.RenderFieldAsync(field, prefix, currentValue, errorMessage);

        Assert.NotNull(result);

        _ = htmlHelper.Received()!.PartialAsync(
            Arg.Any<string>(),
            capturedModel,
            null
        );

        Assert.NotNull(capturedModel);
        var model = Assert.IsType<FieldViewModel>(capturedModel);
        Assert.Equal(field, model.Field);
        Assert.Equal(prefix, model.Prefix);
        Assert.Equal(currentValue, model.CurrentValue);
        Assert.Equal(errorMessage, model.ErrorMessage);
    }

    [Theory]
    [InlineData("text", "~/Views/Shared/Fields/_TextField.cshtml")]
    [InlineData("email", "~/Views/Shared/Fields/_EmailField.cshtml")]
    [InlineData("select", "~/Views/Shared/Fields/_SelectField.cshtml")]
    [InlineData("text-area", "~/Views/Shared/Fields/_TextAreaField.cshtml")]
    [InlineData("radios", "~/Views/Shared/Fields/_RadiosField.cshtml")]
    [InlineData("character-count", "~/Views/Shared/Fields/_CharacterCountField.cshtml")]
    [InlineData("date", "~/Views/Shared/Fields/_DateInputField.cshtml")]
    [InlineData("autocomplete", "~/Views/Shared/Fields/_AutocompleteField.cshtml")]
    [InlineData("complexField", "~/Views/Shared/Fields/_ComplexField.cshtml")]
    public async Task RenderFieldAsync_returns_expected_view_name(string fieldType, string expectedViewName)
    {
        var htmlHelper = Substitute.For([typeof(IHtmlHelper), typeof(IViewContextAware)], []) as IHtmlHelper;
        _serviceProvider.GetService(typeof(IHtmlHelper)).Returns(htmlHelper);

        var field = _fixture.Build<Field>().With(f => f.Type, fieldType).Create();
        var prefix = _fixture.Create<string>();
        var currentValue = _fixture.Create<string>();
        var errorMessage = _fixture.Create<string>();

        var result = await _service.RenderFieldAsync(field, prefix, currentValue, errorMessage);

        Assert.NotNull(result);

        _ = htmlHelper.Received()!.PartialAsync(
            expectedViewName,
            Arg.Any<object?>(),
            null
        );
    }

    [Fact]
    public async Task RenderFieldAsync_throws_NotSupportedException_when_field_type_is_not_supported()
    {
        var htmlHelper = Substitute.For([typeof(IHtmlHelper), typeof(IViewContextAware)], []) as IHtmlHelper;
        _serviceProvider.GetService(typeof(IHtmlHelper)).Returns(htmlHelper);

        var field = _fixture.Create<Field>();
        var prefix = _fixture.Create<string>();
        var currentValue = _fixture.Create<string>();
        var errorMessage = _fixture.Create<string>();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _service.RenderFieldAsync(field, prefix, currentValue, errorMessage));
    }
}