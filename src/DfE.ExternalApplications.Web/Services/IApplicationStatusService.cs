using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;

namespace DfE.ExternalApplications.Web.Services
{
    public interface IApplicationStatusService
    {
        Task<IReadOnlyList<CustomApplicationStatusDto>> GetCustomApplicationStatusesAsync(Guid? templateId);
        KeyValuePair<ApplicationStatus, string> GetCalculatedApplicationStatusAsync(ApplicationDto application, IReadOnlyList<CustomApplicationStatusDto> customStatuses);
        string GetStatusLabel(ApplicationStatus status, IReadOnlyList<CustomApplicationStatusDto> customStatuses);
        string GetBaseStatusLabel(ApplicationStatus status);
        Task OverrideApplicationStatusLabels(CustomApplicationStatusDto customStatus);
    }
}
