namespace DfE.ExternalApplications.Domain.Models
{
    public class ComplexFieldConfiguration
    {
        public string ApiEndpoint { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public bool AllowMultiple { get; set; } = false;
        public int MinLength { get; set; } = 3;
        public string Placeholder { get; set; } = "Start typing to search...";
        public int MaxSelections { get; set; } = 0; // 0 means no limit
    }
} 