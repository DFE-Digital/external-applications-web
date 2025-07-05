using DfE.ExternalApplications.Domain.Models;
using Microsoft.AspNetCore.Http;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Application.Interfaces;

public interface IApplicationResponseService
{
    Task SaveApplicationResponseAsync(Guid applicationId, Dictionary<string, object> formData, ISession session, CancellationToken cancellationToken = default);
    string TransformToResponseJson(Dictionary<string, object> formData, IEnumerable<Field> pageFields);
    void AccumulateFormData(Dictionary<string, object> newData, ISession session);
    Dictionary<string, object> GetAccumulatedFormData(ISession session);
    void ClearAccumulatedFormData(ISession session);
} 