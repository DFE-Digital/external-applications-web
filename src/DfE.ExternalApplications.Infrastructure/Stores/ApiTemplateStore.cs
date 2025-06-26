using DfE.ExternalApplications.Application.Interfaces;

namespace DfE.ExternalApplications.Infrastructure.Stores;

public class ApiTemplateStore(HttpClient client) : ITemplateStore
{
    public async Task<Stream> GetTemplateStreamAsync(string templateId, CancellationToken cancellationToken = default)
    {
        var response = await client.GetAsync($"templates/{templateId}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }
}