using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using Task = System.Threading.Tasks.Task;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.CoreLibs.Caching.Helpers;

namespace DfE.ExternalApplications.Web.Pages.Admin;

[Authorize(Roles = "Admin")]
public class TemplateManagerModel : PageModel
{
    private readonly IFormTemplateProvider _formTemplateProvider;
    private readonly ITemplatesClient _templatesClient;
    private readonly ICacheService<IMemoryCacheType> _cacheService;
    private readonly ITemplateValidationService _templateValidationService;
    private readonly ILogger<TemplateManagerModel> _logger;

    public TemplateManagerModel(
        IFormTemplateProvider formTemplateProvider,
        ITemplatesClient templatesClient,
        ICacheService<IMemoryCacheType> cacheService,
        ITemplateValidationService templateValidationService,
        ILogger<TemplateManagerModel> logger)
    {
        _formTemplateProvider = formTemplateProvider;
        _templatesClient = templatesClient;
        _cacheService = cacheService;
        _templateValidationService = templateValidationService;
        _logger = logger;
    }

    public FormTemplate? CurrentTemplate { get; set; }
    public string? CurrentVersionNumber { get; set; }
    public string? CurrentTemplateJson { get; set; }
    public bool ShowAddVersionForm { get; set; }
    public bool HasError { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public bool ShowSuccess { get; set; }
    public bool ShowCacheCleared { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Version number is required")]
    public string? NewVersion { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "JSON schema is required")]
    public string? NewSchema { get; set; }

    public async Task<IActionResult> OnGetAsync(bool showForm = false, bool success = false, bool cleared = false, string? suggestedVersion = null)
    {
        ShowAddVersionForm = showForm;
        ShowSuccess = success;
        ShowCacheCleared = cleared;

        var templateId = HttpContext.Session.GetString("TemplateId");
        if (string.IsNullOrEmpty(templateId))
        {
            _logger.LogWarning("TemplateId not found in session.");
            return RedirectToPage("/Index");
        }

        await LoadTemplateDataAsync(templateId);
        
        // If a suggested version is provided, use it to pre-populate the NewVersion field
        if (!string.IsNullOrEmpty(suggestedVersion))
        {
            NewVersion = suggestedVersion;
            _logger.LogInformation("Pre-populated NewVersion field with suggested version: {SuggestedVersion}", suggestedVersion);
        }
        
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var templateId = HttpContext.Session.GetString("TemplateId");
        if (string.IsNullOrEmpty(templateId))
        {
            _logger.LogWarning("TemplateId not found in session during post.");
            return RedirectToPage("/Index");
        }

        if (!ValidateInput())
        {
            ShowAddVersionForm = true;
            await LoadTemplateDataAsync(templateId);
            return Page();
        }


        await CreateNewTemplateVersionAsync(templateId);

        await Task.Delay(2000);

        await InvalidateTemplateCacheAsync(templateId);

        _logger.LogInformation("Successfully created template version {NewVersion} for {TemplateId}",
            NewVersion, templateId);

        return RedirectToPage(new { success = true });

}

    public async Task<IActionResult> OnPostShowAddFormAsync()
    {
        // Pre-populate the NewVersion field with auto-incremented version
        var templateId = HttpContext.Session.GetString("TemplateId");
        if (!string.IsNullOrEmpty(templateId))
        {
            await LoadTemplateDataAsync(templateId);
            
            if (!string.IsNullOrEmpty(CurrentVersionNumber))
            {
                var incrementedVersion = IncrementPatchVersion(CurrentVersionNumber);
                _logger.LogInformation("Auto-incremented version from {CurrentVersion} to {NewVersion}", 
                    CurrentVersionNumber, incrementedVersion);
                
                // Pass the auto-incremented version via query parameter
                return RedirectToPage(new { showForm = true, suggestedVersion = incrementedVersion });
            }
        }
        
        return RedirectToPage(new { showForm = true });
    }
    
    /// <summary>
    /// Increments the patch version of a semantic version string (e.g., 1.0.1 -> 1.0.2)
    /// </summary>
    private static string IncrementPatchVersion(string version)
    {
        try
        {
            var parts = version.Split('.');
            
            if (parts.Length == 0)
            {
                return "1.0.1";
            }
            else if (parts.Length == 1)
            {
                // If only major version exists (e.g., "1"), add minor and patch
                return $"{parts[0]}.0.1";
            }
            else if (parts.Length == 2)
            {
                // If major.minor exists (e.g., "1.0"), add patch as 1
                return $"{parts[0]}.{parts[1]}.1";
            }
            else
            {
                // Full semantic version (e.g., "1.0.1")
                // Increment the patch version
                if (int.TryParse(parts[2], out var patchVersion))
                {
                    patchVersion++;
                    return $"{parts[0]}.{parts[1]}.{patchVersion}";
                }
                else
                {
                    // If patch is not a number, default to adding .1
                    return $"{parts[0]}.{parts[1]}.1";
                }
            }
        }
        catch
        {
            // If anything goes wrong, return a sensible default
            return "1.0.1";
        }
    }

    public IActionResult OnPostCancelAdd()
    {
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostClearAllAsync()
    {
        try
        {
            // Clear all session data
            HttpContext.Session.Clear();
            
            // Clear template cache
            var templateId = HttpContext.Session.GetString("TemplateId");
            if (!string.IsNullOrEmpty(templateId))
            {
                var cacheKey = $"FormTemplate_{CacheKeyHelper.GenerateHashedCacheKey(templateId)}";
                _cacheService.Remove(cacheKey);
                _logger.LogInformation("Cleared template cache for key: {CacheKey}", cacheKey);
            }

            _logger.LogInformation("Successfully cleared all sessions and caches from TemplateManager");
            
            // Redirect back to template manager with success message
            return RedirectToPage(new { success = true, cleared = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing sessions and caches from TemplateManager");
            HasError = true;
            ErrorMessage = "Failed to clear sessions and caches.";
            return Page();
        }
    }

    private async Task LoadTemplateDataAsync(string templateId)
    {
        try
        {
            _logger.LogDebug("Loading template data for {TemplateId}", templateId);
            
            var apiResponse = await _templatesClient.GetLatestTemplateSchemaAsync(new Guid(templateId));
            CurrentVersionNumber = apiResponse.VersionNumber;
            
            _logger.LogDebug("API returned template version {VersionNumber} for {TemplateId}", 
                CurrentVersionNumber, templateId);
            
            // Clear cache before loading to ensure we get the latest template
            var cacheKey = $"FormTemplate_{CacheKeyHelper.GenerateHashedCacheKey(templateId)}";
            _cacheService.Remove(cacheKey);
            _logger.LogDebug("Cleared template cache for {TemplateId} to ensure latest version is loaded", templateId);
            
            CurrentTemplate = await _formTemplateProvider.GetTemplateAsync(templateId);
            if (CurrentTemplate != null)
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                CurrentTemplateJson = JsonSerializer.Serialize(CurrentTemplate, options);
                
                _logger.LogDebug("Successfully loaded template {TemplateId} with {TaskGroupCount} task groups", 
                    templateId, CurrentTemplate.TaskGroups?.Count ?? 0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading template data for {TemplateId}", templateId);
            HasError = true;
            ErrorMessage = "There was an error loading the template data.";
        }
    }

    private bool ValidateInput()
    {
        var isValid = true;

        if (string.IsNullOrWhiteSpace(NewVersion))
        {
            ModelState.AddModelError(nameof(NewVersion), "Version number is required");
            isValid = false;
        }

        if (string.IsNullOrWhiteSpace(NewSchema))
        {
            ModelState.AddModelError(nameof(NewSchema), "JSON schema is required");
            isValid = false;
        }
        else
        {
            // Validate JSON against FormTemplate domain model
            var (templateIsValid, validationErrors) = _templateValidationService.ValidateTemplateJson(NewSchema);
            
            if (!templateIsValid)
            {
                _logger.LogWarning("Template validation failed with {ErrorCount} errors", validationErrors.Count);
                
                // Add all validation errors to ModelState
                foreach (var error in validationErrors)
                {
                    ModelState.AddModelError(nameof(NewSchema), error);
                }
                
                isValid = false;
            }
            else
            {
                _logger.LogInformation("Template validation passed successfully");
            }
        }

        return isValid;
    }

    private async Task CreateNewTemplateVersionAsync(string templateId)
    {
        var base64Schema = Convert.ToBase64String(Encoding.UTF8.GetBytes(NewSchema!));
        await _templatesClient.CreateTemplateVersionAsync(new Guid(templateId),
            new CreateTemplateVersionRequest(VersionNumber: NewVersion!, JsonSchema: base64Schema));
    }

    private async Task InvalidateTemplateCacheAsync(string templateId)
    {
        try
        {
            var cacheKey = $"FormTemplate_{CacheKeyHelper.GenerateHashedCacheKey(templateId)}";
            _logger.LogInformation("Attempting to invalidate cache for template {TemplateId} with key {CacheKey}", 
                templateId, cacheKey);
            
            _cacheService.Remove(cacheKey);
            _logger.LogInformation("Successfully invalidated cache for template {TemplateId} with key {CacheKey}", 
                templateId, cacheKey);
            
            // Verify the new template version is available by attempting to load it
            await VerifyNewTemplateVersionAsync(templateId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate cache for template {TemplateId}", templateId);
            // Don't throw - cache invalidation failure shouldn't break the operation
        }
    }
    
    private async Task VerifyNewTemplateVersionAsync(string templateId)
    {
        try
        {
            // Try to load the new template version to ensure it's available
            var newTemplate = await _formTemplateProvider.GetTemplateAsync(templateId);
            _logger.LogDebug("Successfully verified new template version is available for {TemplateId}", templateId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify new template version for {TemplateId}", templateId);
        }
    }

} 