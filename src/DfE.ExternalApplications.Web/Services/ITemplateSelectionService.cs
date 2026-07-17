using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;

namespace DfE.ExternalApplications.Web.Services;

/// <summary>
/// Resolves and persists the active template for the current user session.
/// </summary>
public interface ITemplateSelectionService
{
    /// <summary>
    /// Returns templates the current caller may open (live for end users; all for admins).
    /// </summary>
    Task<IReadOnlyList<TemplateDto>> GetSelectableTemplatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the template id currently stored in session, if any.
    /// </summary>
    string? GetSelectedTemplateId(HttpContext httpContext);

    /// <summary>
    /// Sets the active template in session and clears application-scoped session state.
    /// </summary>
    void SelectTemplate(HttpContext httpContext, Guid templateId);

    /// <summary>
    /// Returns true when the session template is present in <paramref name="templates"/>.
    /// </summary>
    bool HasValidSelection(HttpContext httpContext, IReadOnlyList<TemplateDto> templates);
}
