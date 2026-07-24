using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace DfE.ExternalApplications.Web.Diagnostics;

/// <summary>
/// Builds plaintext TenantConfig SQL INSERT scripts from the running Web app's in-memory configuration
/// (configurations/{APPLICATION_NAME} + env + secrets). Does not connect to TenantConfig and does not encrypt.
/// </summary>
public static class TenantConfigSqlExporter
{
    private static readonly Dictionary<string, Guid> DefaultTenantIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Transfers"] = Guid.Parse("11111111-1111-4111-8111-111111111111"),
        ["Lsrp"] = Guid.Parse("22222222-2222-4222-8222-222222222222"),
        ["RGVisits"] = Guid.Parse("33333333-3333-4333-8333-333333333333"),
        ["Visits"] = Guid.Parse("33333333-3333-4333-8333-333333333333"),
    };

    private static readonly HashSet<string> SkipKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Diagnostics"
    };

    private static readonly HashSet<string> SecretCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConnectionStrings",
        "AzureAd",
        "InternalServiceAuth",
        "Email",
        "DfESignIn",
        "EntraSso",
        "FileStorage",
        "NotificationService",
        "TokenRefresh",
        "ExternalApplicationsApiClient",
        "Authorization"
    };

    public static string BuildFromWebConfiguration(
        IConfiguration configuration,
        string applicationName,
        Guid? tenantIdOverride = null)
    {
        if (!TryResolveTenantId(applicationName, configuration, tenantIdOverride, out var tenantId, out var resolveNote))
        {
            return $"-- Could not resolve TenantId for APPLICATION_NAME '{applicationName}'. Pass ?tenantId=...";
        }

        var tenantName = configuration["ApplicationName"]
            ?? applicationName;

        var sb = new StringBuilder();
        sb.AppendLine("-- Generated from external-applications-web in-memory configuration");
        sb.AppendLine($"-- GeneratedAtUtc: {DateTime.UtcNow:O}");
        sb.AppendLine($"-- APPLICATION_NAME: {applicationName}");
        sb.AppendLine($"-- TenantId: {tenantId} ({resolveNote})");
        sb.AppendLine("-- Target: Web");
        sb.AppendLine("-- Secrets are PLAINTEXT; encrypt manually before/after import as needed.");
        sb.AppendLine("SET NOCOUNT ON;");
        sb.AppendLine();

        sb.AppendLine($"-- ===== Tenant: {EscapeSqlComment(tenantName)} ({tenantId}) =====");
        sb.AppendLine($"""
            IF NOT EXISTS (SELECT 1 FROM tenantconfig.Tenants WHERE Id = '{tenantId}')
            BEGIN
              INSERT INTO tenantconfig.Tenants (Id, Name, IsActive, CreatedAtUtc, UpdatedAtUtc)
              VALUES ('{tenantId}', N'{EscapeSql(tenantName)}', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            END
            """);
        sb.AppendLine();

        // Prefer AzureAd:TenantId only as Azure AD directory id — do not confuse with our SaaS tenant id.
        // Hostnames / origins: best-effort from common web keys if present.
        foreach (var origin in ResolveOrigins(configuration))
        {
            sb.AppendLine($"""
                IF NOT EXISTS (
                  SELECT 1 FROM tenantconfig.TenantFrontendOrigins
                  WHERE TenantId = '{tenantId}' AND Origin = N'{EscapeSql(origin)}')
                BEGIN
                  INSERT INTO tenantconfig.TenantFrontendOrigins (Id, TenantId, Origin)
                  VALUES (NEWID(), '{tenantId}', N'{EscapeSql(origin)}');
                END
                """);
            sb.AppendLine();
        }

        foreach (var categorySection in GetRootCategories(configuration))
        {
            if (SkipKeys.Contains(categorySection.Key))
                continue;

            AppendSetting(sb, tenantId, categorySection.Key, "Web", categorySection);
        }

        return sb.ToString();
    }

    private static bool TryResolveTenantId(
        string applicationName,
        IConfiguration configuration,
        Guid? tenantIdOverride,
        out Guid tenantId,
        out string note)
    {
        if (tenantIdOverride.HasValue)
        {
            tenantId = tenantIdOverride.Value;
            note = "from query string";
            return true;
        }

        var fromConfig = configuration["Diagnostics:TenantId"] ?? configuration["ExportTenantConfig:TenantId"];
        if (Guid.TryParse(fromConfig, out tenantId))
        {
            note = "from Diagnostics:TenantId";
            return true;
        }

        // Some configs nest TenantId under AzureAd / Entra — those are AAD directory ids, not SaaS tenant ids.
        // Prefer known APPLICATION_NAME map used by the API Tenants section.
        if (DefaultTenantIds.TryGetValue(applicationName, out tenantId))
        {
            note = "from APPLICATION_NAME default map";
            return true;
        }

        tenantId = Guid.Empty;
        note = "unresolved";
        return false;
    }

    private static IEnumerable<IConfigurationSection> GetRootCategories(IConfiguration configuration)
    {
        // Prefer children of the configuration root. Avoid dumping provider-internal empty nodes.
        return configuration.GetChildren()
            .Where(c => c.GetChildren().Any() || !string.IsNullOrWhiteSpace(c.Value));
    }

    private static IEnumerable<string> ResolveOrigins(IConfiguration configuration)
    {
        var origins = new List<string>();

        var configured = configuration.GetSection("Frontend:Origins").Get<string[]>();
        if (configured is { Length: > 0 })
            origins.AddRange(configured.Where(o => !string.IsNullOrWhiteSpace(o)));

        var single = configuration["Frontend:Origin"];
        if (!string.IsNullOrWhiteSpace(single))
            origins.Add(single);

        var baseUrl = configuration["FrontendSettings:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(baseUrl))
            origins.Add(baseUrl.TrimEnd('/'));

        return origins
            .Select(o => o.Trim().TrimEnd('/'))
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void AppendSetting(
        StringBuilder sb,
        Guid tenantId,
        string category,
        string target,
        IConfigurationSection section)
    {
        if (category.Length > 50)
        {
            sb.AppendLine($"-- Skipping category '{EscapeSqlComment(category)}': name longer than 50 chars");
            return;
        }

        // Skip scalar root values that are not objects (e.g. AllowedHosts string already skipped).
        if (!section.GetChildren().Any())
            return;

        var json = SerializeSectionToJson(section);
        if (string.IsNullOrWhiteSpace(json) || json == "{}" || json == "null")
            return;

        var isSecret = SecretCategories.Contains(category) ? 1 : 0;

        sb.AppendLine($"""
            MERGE tenantconfig.TenantSettings AS t
            USING (SELECT
              '{tenantId}' AS TenantId,
              N'{EscapeSql(category)}' AS Category,
              N'{EscapeSql(target)}' AS Target,
              N'{EscapeSql(json)}' AS Settings,
              CAST({isSecret} AS bit) AS IsSecret) AS s
            ON t.TenantId = s.TenantId AND t.Category = s.Category AND t.Target = s.Target
            WHEN MATCHED THEN
              UPDATE SET Settings = s.Settings, IsSecret = s.IsSecret, UpdatedAtUtc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
              INSERT (Id, TenantId, Category, Target, Settings, IsSecret, CreatedAtUtc, UpdatedAtUtc)
              VALUES (NEWID(), s.TenantId, s.Category, s.Target, s.Settings, s.IsSecret, SYSUTCDATETIME(), SYSUTCDATETIME());
            """);
        sb.AppendLine();
    }

    private static string SerializeSectionToJson(IConfigurationSection section)
    {
        var value = BuildValue(section);
        return JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = false });
    }

    private static object? BuildValue(IConfigurationSection section)
    {
        var children = section.GetChildren().ToList();
        if (children.Count == 0)
            return section.Value;

        if (children.All(c => int.TryParse(c.Key, out _)))
        {
            return children
                .OrderBy(c => int.Parse(c.Key))
                .Select(BuildValue)
                .ToList();
        }

        var dict = new Dictionary<string, object?>();
        foreach (var child in children)
            dict[child.Key] = BuildValue(child);
        return dict;
    }

    private static string EscapeSql(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string EscapeSqlComment(string value) =>
        value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
