using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Client.Contracts;
using System.Text;

namespace DfE.ExternalApplications.Infrastructure.Stores;

public class ApiTemplateStore(ITemplatesClient templateClient) : ITemplateStore
{
    public async Task<Stream> GetTemplateStreamAsync(string templateId, CancellationToken cancellationToken = default)
    {
        // TODO Implement caching
        var response = await templateClient.GetLatestTemplateSchemaAsync(new Guid(templateId), cancellationToken);

        var utf8 = Encoding.UTF8.GetBytes(response.JsonSchema);

        var stream = new MemoryStream(utf8);

        return stream;
    }
}