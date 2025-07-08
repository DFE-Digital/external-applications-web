using DfE.ExternalApplications.Application.Interfaces;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DfE.ExternalApplications.Infrastructure.Stores;

public class ApiTemplateStore(ITemplatesClient templateClient) : ITemplateStore
{
    [ExcludeFromCodeCoverage]
    public async Task<Stream> GetTemplateStreamAsync(string templateId, CancellationToken cancellationToken = default)
    {
        var response = await templateClient.GetLatestTemplateSchemaAsync(new Guid(templateId), cancellationToken);
        var utf8 = Encoding.UTF8.GetBytes(response.JsonSchema);
        return new MemoryStream(utf8);
    }
}