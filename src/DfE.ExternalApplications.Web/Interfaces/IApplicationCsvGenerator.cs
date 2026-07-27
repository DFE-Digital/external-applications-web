namespace DfE.ExternalApplications.Web.Interfaces
{
    public interface IApplicationCsvGenerator
    {
        string Generate(string applicationReference, string applicationData);
    }
}
