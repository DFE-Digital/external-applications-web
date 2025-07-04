using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using DfE.ExternalApplications.Domain.Models;

namespace DfE.ExternalApplications.Domain.Models;

[ExcludeFromCodeCoverage]
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