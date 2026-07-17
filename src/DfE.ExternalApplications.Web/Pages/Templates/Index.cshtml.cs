using DfE.ExternalApplications.Web.Services;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Web.Pages.Templates;

/// <summary>
/// Lets the user choose which tenant template dashboard to open.
/// Admins also see non-live templates for preview.
/// </summary>
[ExcludeFromCodeCoverage]
[Authorize]
public sealed class IndexModel(
    ITemplateSelectionService templateSelectionService,
    ILogger<IndexModel> logger) : PageModel
{
    /// <summary>Templates available to the current user.</summary>
    public IReadOnlyList<TemplateDto> Templates { get; private set; } = [];

    /// <summary>Optional return URL after selection.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>True when the caller is an Admin.</summary>
    public bool IsAdmin { get; private set; }

    /// <summary>Error message when selection fails.</summary>
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        IsAdmin = User.IsInRole("Admin");
        Templates = await templateSelectionService.GetSelectableTemplatesAsync(cancellationToken);

        if (Templates.Count == 1 && !IsAdmin)
        {
            templateSelectionService.SelectTemplate(HttpContext, Templates[0].TemplateId);
            return Redirect(GetSafeReturnUrl());
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSelectAsync(Guid templateId, CancellationToken cancellationToken)
    {
        IsAdmin = User.IsInRole("Admin");
        Templates = await templateSelectionService.GetSelectableTemplatesAsync(cancellationToken);

        if (Templates.All(t => t.TemplateId != templateId))
        {
            ErrorMessage = "You do not have access to that template.";
            logger.LogWarning("User attempted to select inaccessible template {TemplateId}", templateId);
            return Page();
        }

        templateSelectionService.SelectTemplate(HttpContext, templateId);
        return Redirect(GetSafeReturnUrl());
    }

    private string GetSafeReturnUrl()
    {
        if (!string.IsNullOrWhiteSpace(ReturnUrl) &&
            Url.IsLocalUrl(ReturnUrl) &&
            !ReturnUrl.StartsWith("/templates", StringComparison.OrdinalIgnoreCase))
        {
            return ReturnUrl;
        }

        return "/applications/dashboard";
    }
}
