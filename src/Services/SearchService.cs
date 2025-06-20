using DfE.ExternalApplications.Web.Models;
using Newtonsoft.Json;

namespace DfE.ExternalApplications.Web.Services
{
    public interface ISearchService
    {
        public Task<ApiResponse> Execute(string endpoint);
    }

    public class SearchService : ISearchService
    {
        private readonly HttpClient _httpClient;
        public SearchService(HttpClient httpClient, IConfiguration configuration)
        {
            var baseUrl = configuration["AcademiesApi:BaseUrl"];
            var apiKey = configuration["AcademiesApi:ApiKey"];

            httpClient.BaseAddress = new Uri(baseUrl);
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("ApiKey", apiKey);
            
            _httpClient = httpClient;
        }

        public async Task<ApiResponse> Execute(string endpoint)
        {
            try
            {
                var response = await _httpClient.GetAsync(endpoint);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                dynamic data = JsonConvert.DeserializeObject<dynamic>(json);

                return new ApiResponse(true, data, null);
            }

            catch (Exception ex)
            {
                return new ApiResponse(false, null, ex.Message);
            }

        }
    }
}