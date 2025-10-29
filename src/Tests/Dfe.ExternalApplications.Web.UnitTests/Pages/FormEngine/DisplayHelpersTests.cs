using System.Text.Json;
using AutoFixture;
using DfE.ExternalApplications.Web.Pages.FormEngine;

namespace Dfe.ExternalApplications.Web.UnitTests.Pages.FormEngine;

public class DisplayHelpersTests
{
    private readonly IFixture _fixture = new Fixture();

    [Fact]
    public void ExpandEncodedJson_when_itemData_is_null_then_return_null()
    {
        Dictionary<string, object>? itemData = null;
        
        var result = DisplayHelpers.ExpandEncodedJson(itemData);
        
        Assert.Null(result);
    }

    [Fact]
    public void ExpandEncodedJson_when_itemData_is_empty_then_return_empty_dictionary()
    {
        var itemData = new Dictionary<string, object>();
        
        var result = DisplayHelpers.ExpandEncodedJson(itemData);
        
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ExpandEncodedJson_when_itemData_has_basic_values_then_return_values_unchanged()
    {
        var itemData = new Dictionary<string, object>
        {
            {"foo", "bar"},
            {"baz", 123},
            {"quux", true},
        };
        
        var result = DisplayHelpers.ExpandEncodedJson(itemData);
        
        Assert.NotNull(result);
        Assert.Equal(result["foo"], "bar");
        Assert.Equal(result["baz"], 123);
        Assert.Equal(result["quux"], true);
    }

    [Fact]
    public void ExpandEncodedJson_when_itemData_has_JsonElement_values_then_parse_json_strings_into_JsonElements()
    {
        var itemData = new Dictionary<string, object>
        {
            {"foo", "bar"},
            {"baz", 123},
            {"quux", true},
            {"json", JsonSerializer.Deserialize<JsonElement>("\"{\\\"isJsonString\\\":true}\"")}
        };
        
        var result = DisplayHelpers.ExpandEncodedJson(itemData);

        Assert.NotNull(result);
        Assert.Equal(result["foo"], "bar");
        Assert.Equal(result["baz"], 123);
        Assert.Equal(result["quux"], true);
        Assert.Equivalent(result["json"], JsonSerializer.Deserialize<JsonElement>("{\"isJsonString\":true}"));
    }

    [Fact]
    public void ExpandEncodedJson_when_itemData_has_JsonElement_values_then_return_unparseable_strings_unchanged()
    {
        var itemData = new Dictionary<string, object>
        {
            {"foo", "bar"},
            {"baz", 123},
            {"quux", true},
            {"json", JsonSerializer.Deserialize<JsonElement>("\"Just a regular string\"")}
        };
        
        var result = DisplayHelpers.ExpandEncodedJson(itemData);

        Assert.NotNull(result);
        Assert.Equal(result["foo"], "bar");
        Assert.Equal(result["baz"], 123);
        Assert.Equal(result["quux"], true);
        Assert.Equivalent(result["json"], JsonSerializer.Deserialize<JsonElement>("\"Just a regular string\""));
    }

    [Fact]
    public void ExpandEncodedJson_when_itemData_has_JsonElement_values_that_are_not_strings_then_return_values_unchanged()
    {
        var itemData = new Dictionary<string, object>
        {
            {"foo", "bar"},
            {"baz", 123},
            {"quux", true},
            {"json", JsonSerializer.Deserialize<JsonElement>("{\"isJsonString\":false}")}
        };
        
        var result = DisplayHelpers.ExpandEncodedJson(itemData);
        
        Assert.NotNull(result);
        Assert.Equal(result["foo"], "bar");
        Assert.Equal(result["baz"], 123);
        Assert.Equal(result["quux"], true);
        Assert.Equivalent(result["json"], JsonSerializer.Deserialize<JsonElement>("{\"isJsonString\":false}"));
    }
    
    [Fact]
    public void GenerateSuccessMessage_when_customMessage_is_not_null_then_return_provided_message()
    {
        var customMessage = _fixture.Create<string>();
        var operation = _fixture.Create<string>();
        var itemData = _fixture.Create<Dictionary<string, object>?>();
        var flowTitle = _fixture.Create<string?>();

        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, flowTitle);
        
        Assert.Equal(customMessage, result);
    }
    
    [Theory]
    [InlineData("{flowTitle}", "Some Flow", "Some Flow")]
    [InlineData("Successful {flowTitle}", "Flow 2", "Successful Flow 2")]
    [InlineData("{flowTitle} has been successfully done", "Another Flow", "Another Flow has been successfully done")]
    public void GenerateSuccessMessage_when_customMessage_has_flow_title_interpolation_and_flowTitle_is_not_null_then_return_interpolated_message(string customMessage, string flowTitle, string expected)
    {
        var operation = _fixture.Create<string>();
        var itemData = _fixture.Create<Dictionary<string, object>?>();
        
        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, flowTitle);
        
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("{flowTitle}", "collection")]
    [InlineData("Successful {flowTitle}", "Successful collection")]
    [InlineData("{flowTitle} has been successfully done", "collection has been successfully done")]
    public void GenerateSuccessMessage_when_customMessage_has_flow_title_interpolation_and_flowTitle_is_null_then_return_message_with_default_flow_title(string customMessage, string expected)
    {
        var operation = _fixture.Create<string>();
        var itemData = _fixture.Create<Dictionary<string, object>?>();
        
        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, null);
        
        Assert.Equal(expected, result);
    }
    
    [Theory]
    [InlineData("{foo} was successful", new[] {"foo"}, new[] {"bar"}, "bar was successful")]
    [InlineData("Successfully did {bar} to {quux}", new[] {"bar", "quux"}, new[] {"xyzzy", "bleeb"}, "Successfully did xyzzy to bleeb")]
    public void GenerateSuccessMessage_when_customMessage_has_interpolation_and_itemData_is_not_null_then_return_interpolated_message(string customMessage, string[] interpolationKeys, string[] interpolationValues, string expected)
    {
        var operation = _fixture.Create<string>();
        var flowTitle = _fixture.Create<string?>();
        
        var itemData = new Dictionary<string, object>();
        for (var i = 0; i < interpolationKeys.Length; i++)
        {
            itemData.Add(interpolationKeys[i], interpolationValues[i]);
        }
        
        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, flowTitle);
        
        Assert.Equal(expected, result);
    }
    
    [Theory]
    [InlineData("{foo} was successful")]
    [InlineData("Successfully did {bar} to {quux}")]
    public void GenerateSuccessMessage_when_customMessage_has_interpolation_and_itemData_is_null_then_return_message_with_no_interpolation(string customMessage)
    {
        var operation = _fixture.Create<string>();
        var flowTitle = _fixture.Create<string?>();
        
        Dictionary<string, object>? itemData = null;
        
        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, flowTitle);
        
        Assert.Equal(customMessage, result);
    }

    [Fact]
    public void GenerateSuccessMessage_when_itemData_has_a_key_with_a_JsonElement_value_then_return_interpolated_message()
    {
        var customMessage = "{foo} was successful";
        var operation = _fixture.Create<string>();
        var flowTitle = _fixture.Create<string?>();
        
        var obj = new
        {
            bar = "bar"
        };

        var itemData = new Dictionary<string, object>()
        {
            { "foo", JsonSerializer.SerializeToElement(obj) }
        };
        
        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, flowTitle);
        
        Assert.Equal("{\"bar\":\"bar\"} was successful", result);
    }

    [Fact]
    public void GenerateSuccessMessage_when_itemData_has_a_key_with_a_JsonElement_value_then_subkeys_can_be_interpolated()
    {
        var customMessage = "{foo.bar.baz} was {foo.xyzzy}";
        var operation = _fixture.Create<string>();
        var flowTitle = _fixture.Create<string?>();
        
        var obj = new
        {
            bar = new
            {
                baz = "quux",
                nope = "nope"
            },
            xyzzy = "bleeb",
            nope = "nope"
        };

        var itemData = new Dictionary<string, object>()
        {
            { "foo", JsonSerializer.SerializeToElement(obj) }
        };
        
        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, flowTitle);
        
        Assert.Equal("quux was bleeb", result);
    }

    [Fact]
    public void GenerateSuccessMessage_when_itemData_has_a_key_with_a_JsonElement_value_then_missing_subkeys_are_not_interpolated()
    {
        var customMessage = "{foo.bar.baz} was {foo.xyzzy}";
        var operation = _fixture.Create<string>();
        var flowTitle = _fixture.Create<string?>();
        
        var obj = new
        {
            bar = new
            {
                nope = "nope"
            },
            nope = "nope"
        };

        var itemData = new Dictionary<string, object>()
        {
            { "foo", JsonSerializer.SerializeToElement(obj) }
        };
        
        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, flowTitle);
        
        Assert.Equal("{foo.bar.baz} was {foo.xyzzy}", result);
    }

    [Theory]
    [InlineData("add", "Item has been added to collection")]
    [InlineData("update", "Item has been updated")]
    [InlineData("delete", "Item has been removed from collection")]
    public void GenerateSuccessMessage_when_customMessage_is_null_and_no_display_name_or_flow_title_exists_then_return_default_message(string operation, string expected)
    {
        string? customMessage = null;
        Dictionary<string, object>? itemData = null;
        string? flowTitle = null;
        
        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, flowTitle);
        
        Assert.Equal(expected, result);
    }
    
    [Theory]
    [InlineData("add", "Some Flow", "Item has been added to some flow")]
    [InlineData("update", "Flow 2", "Item has been updated")]
    [InlineData("delete", "Another Flow", "Item has been removed from another flow")]
    public void GenerateSuccessMessage_when_customMessage_is_null_and_no_display_name_exists_then_return_default_message(string operation, string flowTitle, string expected)
    {
        string? customMessage = null;
        Dictionary<string, object>? itemData = null;
        
        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, flowTitle);
        
        Assert.Equal(expected, result);
    }
    
    [Theory]
    [InlineData("add", "firstName", "Someone", "Some Flow", "Someone has been added to some flow")]
    [InlineData("update", "name", "Someone Else", "Flow 2", "Someone Else has been updated")]
    [InlineData("delete", "title", "Something", "Another Flow", "Something has been removed from another flow")]
    [InlineData("add", "label", "Widget", "Flow IV", "Widget has been added to flow iv")]
    [InlineData("update", "foo", "Some Foo", "The Fifth Flow", "Some Foo has been updated")]
    public void GenerateSuccessMessage_when_customMessage_is_null_and_itemData_is_not_empty_then_return_default_message(string operation, string itemDataKey, string itemDataValue, string flowTitle, string expected)
    {
        string? customMessage = null;
        
        var itemData = new Dictionary<string, object>
        {
            {itemDataKey, itemDataValue}
        };
        
        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, flowTitle);
        
        Assert.Equal(expected, result);
    }
    
    [Theory]
    [InlineData("add", "firstName", "Someone", "Some Flow", "Someone has been added to some flow")]
    [InlineData("update", "name", "Someone Else", "Flow 2", "Someone Else has been updated")]
    [InlineData("delete", "title", "Something", "Another Flow", "Something has been removed from another flow")]
    [InlineData("add", "label", "Widget", "Flow IV", "Widget has been added to flow iv")]
    [InlineData("update", "quux", "Not Bar", "The Fifth Flow", "Bar has been updated")]
    public void GenerateSuccessMessage_when_customMessage_is_null_and_itemData_has_common_name_field_then_that_field_has_priority(string operation, string itemDataKey, string itemDataValue, string flowTitle, string expected)
    {
        string? customMessage = null;
        
        var itemData = new Dictionary<string, object>
        {
            {"foo", "Bar"},
            {itemDataKey, itemDataValue},
        };
        
        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, flowTitle);
        
        Assert.Equal(expected, result);
    }
    
    [Fact]
    public void GenerateSuccessMessage_when_customMessage_is_null_and_operation_is_unknown_then_return_default_message()
    {
        string? customMessage = null;
        var operation = _fixture.Create<string>();
        Dictionary<string, object>? itemData = null;
        string? flowTitle = null;
        
        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, flowTitle);
        
        Assert.Equal("Item has been processed", result);
    }

    [Theory]
    [InlineData("firstName", "Someone", "Someone has been processed")]
    [InlineData("name", "Someone Else", "Someone Else has been processed")]
    [InlineData("title", "Something", "Something has been processed")]
    [InlineData("label", "Widget", "Widget has been processed")]
    [InlineData("foo", "Some Foo", "Some Foo has been processed")]
    public void GenerateSuccessMessage_when_customMessage_is_null_and_operation_is_unknown_and_itemData_is_not_empty_then_return_default_message(string itemDataKey, string itemDataValue, string expected)
    {
        string? customMessage = null;
        var operation = _fixture.Create<string>();
        string? flowTitle = null;
        
        var itemData = new Dictionary<string, object>
        {
            {itemDataKey, itemDataValue}
        };
        
        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, flowTitle);
        
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("firstName", "Someone", "Someone has been processed")]
    [InlineData("name", "Someone Else", "Someone Else has been processed")]
    [InlineData("title", "Something", "Something has been processed")]
    [InlineData("label", "Widget", "Widget has been processed")]
    [InlineData("quux", "Not Bar", "Bar has been processed")]
    public void GenerateSuccessMessage_when_customMessage_is_null_and_operation_is_unknown_and_itemData_has_common_name_field_then_that_field_has_priority(string itemDataKey, string itemDataValue, string expected)
    {
        string? customMessage = null;
        var operation = _fixture.Create<string>();
        var flowTitle = _fixture.Create<string?>();
        
        var itemData = new Dictionary<string, object>
        {
            {"foo", "Bar"},
            {itemDataKey, itemDataValue},
        };
        
        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, flowTitle);
        
        Assert.Equal(expected, result);
    }
    
    [Theory]
    [InlineData("Data[incomingTrustsSearch-field-flow]", "Someone", "Someone was successfully added to the thing")]
    [InlineData("CurrentTask.TaskName", "Some Task", "Some Task was successfully added to the thing")]
    public void GenerateSuccessMessage_can_handle_itemData_with_weird_keys_and_json_values(string itemDataKey, string itemDataValue, string expected)
    {
        var customMessage = $"{{{itemDataKey}}} was successfully added to the thing";
        var operation = _fixture.Create<string>();
        var flowTitle = _fixture.Create<string?>();
        
        var itemData = new Dictionary<string, object>
        {
            {itemDataKey, JsonSerializer.SerializeToElement(itemDataValue)}
        };
        
        var result = DisplayHelpers.GenerateSuccessMessage(customMessage, operation, itemData, flowTitle);
        
        Assert.Equal(expected, result);
    }
    
    [Fact]
    public void SanitiseHtmlInput_normalises_newlines_to_br_tags()
    {
        var input = "Some\r\nnew\rlines\nhere";
        var result = DisplayHelpers.SanitiseHtmlInput(input);
        
        Assert.Equal("Some<br>new<br>lines<br>here", result);
    }

    [Fact]
    public void SanitiseHtmlInput_escapes_html_characters()
    {
        var input = "<script>alert('hello')</script>";
        var result = DisplayHelpers.SanitiseHtmlInput(input);
        
        Assert.Equal("&lt;script&gt;alert(&#x27;hello&#x27;)&lt;/script&gt;", result);
    }

    [Fact]
    public void SanitiseHtmlInput_escapes_characters_outside_the_latin_set()
    {
        var input = "👍";
        var result = DisplayHelpers.SanitiseHtmlInput(input);
        
        Assert.Equal("&#x1F44D;", result);
    }
}