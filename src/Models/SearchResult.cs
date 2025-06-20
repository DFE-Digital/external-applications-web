using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Web.Models
{
    public class SearchResult
    {
        public required string? Label { get; set; }

        public required string? Hint { get; set; }
    }
}
