namespace DfE.ExternalApplications.Web.Interfaces
{
    public interface IApplicationCsvGenerator
    {
        Stream? Generate(string html);
        string? GenerateJson(string html);
    }
}
