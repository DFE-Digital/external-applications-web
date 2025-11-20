using AutoFixture;
using AutoFixture.AutoNSubstitute;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Infrastructure.Services;

namespace DfE.ExternalApplications.Infrastructure.UnitTests.Services;

public class DerivedCollectionFlowServiceTests
{
    private readonly IFixture _fixture;
    private readonly DerivedCollectionFlowService _service;

    public DerivedCollectionFlowServiceTests()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization { ConfigureMembers = true });
        
        _fixture.Customize<Condition>(ob => ob.Without(rule => rule.Conditions));
        
        _service = _fixture.Create<DerivedCollectionFlowService>();
    }
    
    [Fact]
    public void GenerateItemsFromSourceField_for_collection_correctly_decodes_sourceJson()
    {
        var fieldId = _fixture.Create<string>();
        var sourceJson = "[{\"foo\":{\"id\":\"123456\",\"name\":\"some foo\"},\"bar\":123}]";
        var formData = new Dictionary<string, object>
        {
            [fieldId] = sourceJson,
        };
        var config = _fixture.Build<DerivedCollectionFlowConfiguration>()
            .With(c => c.SourceType, "collection")
            .Create();

        var result = _service.GenerateItemsFromSourceField(fieldId, formData, config);
        
        Assert.NotEmpty(result);
        var item = Assert.Single(result);
        Assert.Equal("123456", item.Id);
        Assert.Equal("some foo", item.DisplayName);
        Assert.Equal("Not signed yet", item.Status);
    }
    
    [Fact]
    public void GenerateItemsFromSourceField_for_collection_correctly_html_escapes_sourceJson()
    {
        var fieldId = _fixture.Create<string>();
        var sourceJson = "[{\"foo\":\"{&quot;id&quot;:&quot;123456&quot;,&quot;name&quot;:&quot;some foo&quot;}\",\"bar\":123}]";
        var formData = new Dictionary<string, object>
        {
            [fieldId] = sourceJson,
        };
        var config = _fixture.Build<DerivedCollectionFlowConfiguration>()
            .With(c => c.SourceType, "collection")
            .Create();

        var result = _service.GenerateItemsFromSourceField(fieldId, formData, config);
        
        Assert.NotEmpty(result);
        var item = Assert.Single(result);
        Assert.Equal("123456", item.Id);
        Assert.Equal("some foo", item.DisplayName);
        Assert.Equal("Not signed yet", item.Status);
    }
}