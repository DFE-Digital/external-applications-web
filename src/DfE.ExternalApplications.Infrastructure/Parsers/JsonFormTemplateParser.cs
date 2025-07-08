using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace DfE.ExternalApplications.Infrastructure.Parsers;

public class JsonFormTemplateParser : IFormTemplateParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [ExcludeFromCodeCoverage]
    public async Task<FormTemplate> ParseAsync(Stream templateStream, CancellationToken cancellationToken = default)
    {
        var template = await JsonSerializer.DeserializeAsync<FormTemplate>(templateStream, JsonOptions, cancellationToken);
        return template ?? throw new InvalidOperationException("Template could not be parsed.");
    }
}