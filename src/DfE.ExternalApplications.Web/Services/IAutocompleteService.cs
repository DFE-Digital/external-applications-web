namespace DfE.ExternalApplications.Web.Services
{
    public interface IAutocompleteService
    {
        Task<List<object>> SearchAsync(string endpoint, string query);
    }
} 