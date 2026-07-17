using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;

namespace DfE.ExternalApplications.Web.Services;

/// <inheritdoc />
public sealed class TemplateSelectionService(
    ITemplatesClient templatesClient,
    ILogger<TemplateSelectionService> logger) : ITemplateSelectionService
{
    private const string TemplateIdSessionKey = "TemplateId";
    private static readonly string[] ApplicationSessionKeysToClear =
    [
        "ApplicationId",
        "ApplicationReference",
        "FormData",
        "CurrentTaskId",
        "CurrentPageId"
    ];

    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateDto>> GetSelectableTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        var templates = await templatesClient.GetAccessibleTemplatesAsync(cancellationToken);
        return templates
            .OrderByDescending(t => t.IsLive)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public string? GetSelectedTemplateId(HttpContext httpContext)
        => httpContext.Session.GetString(TemplateIdSessionKey);

    /// <inheritdoc />
    public void SelectTemplate(HttpContext httpContext, Guid templateId)
    {
        var previous = httpContext.Session.GetString(TemplateIdSessionKey);
        var next = templateId.ToString();

        if (!string.Equals(previous, next, StringComparison.OrdinalIgnoreCase))
        {
            ClearApplicationSessionState(httpContext.Session);
        }

        httpContext.Session.SetString(TemplateIdSessionKey, next);
        logger.LogInformation("Selected template {TemplateId} for session", next);
    }

    /// <inheritdoc />
    public bool HasValidSelection(HttpContext httpContext, IReadOnlyList<TemplateDto> templates)
    {
        var selected = GetSelectedTemplateId(httpContext);
        if (string.IsNullOrWhiteSpace(selected) || !Guid.TryParse(selected, out var selectedId))
        {
            return false;
        }

        return templates.Any(t => t.TemplateId == selectedId);
    }

    private static void ClearApplicationSessionState(ISession session)
    {
        foreach (var key in ApplicationSessionKeysToClear)
        {
            session.Remove(key);
        }

        foreach (var key in session.Keys.Where(k =>
                     k.StartsWith("ApplicationStatus_", StringComparison.OrdinalIgnoreCase) ||
                     k.StartsWith("FormAccumulation_", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            session.Remove(key);
        }
    }
}
