using System.Diagnostics.CodeAnalysis;
using System.Text;
using DfE.ExternalApplications.Web.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.ExternalApplications.Web.Controllers;

/// <summary>
/// Diagnostics endpoints for migration helpers (e.g. dumping in-memory config as TenantConfig SQL).
/// </summary>
[Route("diagnostics")]
[Authorize(Roles = "Admin")]
[ExcludeFromCodeCoverage]
public class DiagnosticsController(
    IConfiguration configuration,
    ILogger<DiagnosticsController> logger) : ControllerBase
{
    /// <summary>
    /// Dumps in-memory Web configuration as TenantConfig SQL (plaintext, no DB write).
    /// Requires Admin role. Enable with Diagnostics:ExportTenantConfigSqlEnabled=true.
    /// Optional: ?tenantId=... to override the default APPLICATION_NAME → tenant GUID map.
    /// SQL is written to the container console and returned as text/plain.
    /// </summary>
    [HttpGet("export-tenant-config-sql")]
    public IActionResult ExportTenantConfigSql([FromQuery] Guid? tenantId = null)
    {
        if (!configuration.GetValue("Diagnostics:ExportTenantConfigSqlEnabled", false))
        {
            return NotFound(new
            {
                message = "Export disabled. Set Diagnostics:ExportTenantConfigSqlEnabled=true to enable."
            });
        }

        var applicationName = Environment.GetEnvironmentVariable("APPLICATION_NAME")
            ?? configuration["ApplicationName"]
            ?? "Transfers";

        var sql = TenantConfigSqlExporter.BuildFromWebConfiguration(
            configuration,
            applicationName,
            tenantId);

        logger.LogWarning(
            "TenantConfig SQL export requested for APPLICATION_NAME={ApplicationName}. Script length={Length}. Full script follows in console output.",
            applicationName,
            sql.Length);

        Console.WriteLine();
        Console.WriteLine($"========== BEGIN TenantConfig SQL (Web / {applicationName}) ==========");
        Console.WriteLine(sql);
        Console.WriteLine($"========== END TenantConfig SQL (Web / {applicationName}) ==========");
        Console.WriteLine();

        return Content(sql, "text/plain", Encoding.UTF8);
    }
}
