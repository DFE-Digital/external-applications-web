using static DfE.ExternalApplications.Web.Services.ApplicationCsvGenerator;

namespace DfE.ExternalApplications.Web.Interfaces
{
    public interface IApplicationCsvGenerator
    {
        Csv Generate(string appRef, IDictionary<string, object> fields);
    }
}
