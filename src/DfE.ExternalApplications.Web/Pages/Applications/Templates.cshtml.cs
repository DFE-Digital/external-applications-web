using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace DfE.ExternalApplications.Web.Pages.Applications;

[Authorize(Roles = "Admin, Caseworker")]
public class TemplatesModel(IConfiguration configuration) : PageModel
{
    public IEnumerable<ApplicationTemplate> ApplicationTemplates { get; private set; } = [];

    private readonly string? connectionString = configuration.GetConnectionString("Test");

    public void OnGet()
    {
        // HACK get templates from API! - remove Microsoft.Data.SqlClient nuget package reference
        using SqlConnection connection = new(connectionString);
        SqlCommand command = new("SELECT * FROM ea.TemplateVersions ORDER BY CreatedOn DESC", connection);
        connection.Open();
        SqlDataReader reader = command.ExecuteReader();
        List<ApplicationTemplate> templateVersions = [];
        while (reader.Read())
        {
            ApplicationTemplate templateVersion = new()
            {
                VersionId = (Guid)reader["TemplateVersionId"],
                Id = (Guid)reader["TemplateId"],
                VersionNumber = (string)reader["VersionNumber"],
            };
            templateVersions.Add(templateVersion);
        }
        ApplicationTemplates = templateVersions;
        reader.Close();
    }
}

public class ApplicationTemplate
{
    public Guid VersionId { get; set; }
    public Guid Id { get; set; }
    public string? VersionNumber { get; set; }
}
