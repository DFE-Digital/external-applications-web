using DfE.ExternalApplications.Domain.Models;
using Microsoft.AspNetCore.Http;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Application.Interfaces;

public interface IApplicationResponseService
{
    Task SaveApplicationResponseAsync(Guid applicationId, Dictionary<string, object> formData, ISession session, CancellationToken cancellationToken = default);
    string TransformToResponseJson(Dictionary<string, object> formData, Dictionary<string, string> taskStatusData);
    void AccumulateFormData(Dictionary<string, object> newData, ISession session);
    Dictionary<string, object> GetAccumulatedFormData(ISession session);
    void ClearAccumulatedFormData(ISession session);
    Dictionary<string, string> GetTaskStatusFromSession(Guid applicationId, ISession session);
    void SaveTaskStatusToSession(Guid applicationId, string taskId, string status, ISession session);
} 