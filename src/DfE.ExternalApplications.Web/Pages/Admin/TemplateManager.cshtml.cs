using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Request;
using Task = System.Threading.Tasks.Task;
using DfE.CoreLibs.Caching.Interfaces;
using DfE.CoreLibs.Caching.Helpers;

namespace DfE.ExternalApplications.Web.Pages.Admin;

[Authorize(Roles = "Admin")]
public class TemplateManagerModel : PageModel
{
    private readonly IFormTemplateProvider _formTemplateProvider;
    private readonly ITemplatesClient _templatesClient;
    private readonly ICacheService<IMemoryCacheType> _cacheService;
    private readonly IApiErrorParser _apiErrorParser;
    private readonly IModelStateErrorHandler _errorHandler;
    private readonly ILogger<TemplateManagerModel> _logger;

    public TemplateManagerModel(
        IFormTemplateProvider formTemplateProvider,
        ITemplatesClient templatesClient,
        ICacheService<IMemoryCacheType> cacheService,
        IApiErrorParser apiErrorParser,
        IModelStateErrorHandler errorHandler,
        ILogger<TemplateManagerModel> logger)
    {
        _formTemplateProvider = formTemplateProvider;
        _templatesClient = templatesClient;
        _cacheService = cacheService;
        _apiErrorParser = apiErrorParser;
        _errorHandler = errorHandler;
        _logger = logger;
    }

    public FormTemplate? CurrentTemplate { get; set; }
    public string? CurrentVersionNumber { get; set; }
    public string? CurrentTemplateJson { get; set; }
    public bool ShowAddVersionForm { get; set; }
    public bool HasError { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public bool ShowSuccess { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Version number is required")]
    public string? NewVersion { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "JSON schema is required")]
    public string? NewSchema { get; set; }

    public async Task<IActionResult> OnGetAsync(bool showForm = false, bool success = false)
    {
        ShowAddVersionForm = showForm;
        ShowSuccess = success;
        
        var templateId = HttpContext.Session.GetString("TemplateId");
        if (string.IsNullOrEmpty(templateId))
        {
            _logger.LogWarning("TemplateId not found in session.");
            return RedirectToPage("/Index");
        }

        await LoadTemplateDataAsync(templateId);
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

        try
        {
            await CreateNewTemplateVersionAsync(templateId);
            
            await Task.Delay(2000);
            
            await InvalidateTemplateCacheAsync(templateId);
            
            _logger.LogInformation("Successfully created template version {NewVersion} for {TemplateId}", 
                NewVersion, templateId);
            
            return RedirectToPage(new { success = true });
        }
        catch (Exception ex)
        {
            HandleApiError(ex);
            ShowAddVersionForm = true;
            await LoadTemplateDataAsync(templateId);
            return Page();
        }
    }

    public IActionResult OnPostShowAddForm()
    {
        return RedirectToPage(new { showForm = true });
    }

    public IActionResult OnPostCancelAdd()
    {
        return RedirectToPage();
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
        else if (!IsValidJson(NewSchema))
        {
            ModelState.AddModelError(nameof(NewSchema), "Invalid JSON format. Please check your schema syntax.");
            isValid = false;
        }

        return isValid;
    }

    private static bool IsValidJson(string jsonString)
    {
        try
        {
            JsonSerializer.Deserialize<JsonElement>(jsonString);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
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

    private void HandleApiError(Exception exception)
    {
        _logger.LogError(exception, "API error occurred while creating template version");
        
        var parseResult = _apiErrorParser.ParseApiError(exception);
        
        if (parseResult.IsSuccess && parseResult.ErrorResponse != null)
        {
            _errorHandler.AddApiErrorsToModelState(ModelState, parseResult.ErrorResponse);
        }
        else
        {
            var fallbackMessage = exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                ? $"Version {NewVersion} already exists. Please use a different version number."
                : $"There was an error saving the new template version: {parseResult.RawError ?? exception.Message}";
                
            _errorHandler.AddGeneralError(ModelState, fallbackMessage);
        }
    }
} 