using DfE.ExternalApplications.Web.Services;

namespace DfE.ExternalApplications.Web.Interfaces
{
    public interface IApplicationImporter
    {
        Task<ApplicationImportResult> ImportSpreadsheet(Guid templateId, Stream stream);
    }
}
