using DfE.ExternalApplications.Domain.Models;

namespace DfE.ExternalApplications.Application.Interfaces;

public interface IFormTemplateParser
{
    Task<FormTemplate> ParseAsync(Stream templateStream, CancellationToken cancellationToken = default);
}