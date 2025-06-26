namespace DfE.ExternalApplications.Application.Interfaces;

public interface ITemplateStore
{
    Task<Stream> GetTemplateStreamAsync(string templateId, CancellationToken cancellationToken = default);
}
