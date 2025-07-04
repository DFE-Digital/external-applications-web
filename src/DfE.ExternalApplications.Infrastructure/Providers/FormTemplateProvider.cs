using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Infrastructure.Providers;

public class FormTemplateProvider(ITemplateStore store, IFormTemplateParser parser) : IFormTemplateProvider
{
    [ExcludeFromCodeCoverage]
    public async Task<FormTemplate> GetTemplateAsync(string templateId, CancellationToken cancellationToken = default)
    {
        using var stream = await store.GetTemplateStreamAsync(templateId, cancellationToken);
        return await parser.ParseAsync(stream, cancellationToken);
    }
}