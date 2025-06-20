using System.Text.Json.Serialization;

namespace DfE.ExternalApplications.Web.Models
{
    public class Application
    {
        [JsonPropertyName("referenceNumber")]
        public required string ReferenceNumber { get; set; }

        [JsonPropertyName("dateStarted")]
        public required DateTime DateStarted { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

    }
}
