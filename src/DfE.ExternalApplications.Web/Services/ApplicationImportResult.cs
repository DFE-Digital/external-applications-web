namespace DfE.ExternalApplications.Web.Services
{
    public class ApplicationImportResult
    {
        public bool Success { get; set; }
        public IEnumerable<string>? Errors { get; set; }
        public int FieldCount { get; set; }
        public Guid? ApplicationId { get; set; }
    }
}
