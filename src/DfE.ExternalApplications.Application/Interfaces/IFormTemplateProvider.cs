using DfE.ExternalApplications.Domain.Models;

namespace DfE.ExternalApplications.Application.Interfaces;

public interface IFormTemplateProvider
{
    Task<FormTemplate> GetTemplateAsync(string templateId, CancellationToken cancellationToken = default);
}