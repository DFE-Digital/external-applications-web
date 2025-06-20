namespace DfE.ExternalApplications.Web.Models
{
    public record ApiResponse(bool Success, dynamic? Data, string? ErrorMessage);
}
