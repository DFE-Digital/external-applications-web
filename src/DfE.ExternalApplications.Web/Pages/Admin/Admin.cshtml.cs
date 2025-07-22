using DfE.CoreLibs.Caching.Helpers;
using DfE.CoreLibs.Caching.Interfaces;
using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics.CodeAnalysis;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Web.Pages.Admin
{
    [ExcludeFromCodeCoverage]
    [Authorize(Roles = "Admin")]
    public class AdminModel : PageModel
    {
        public string? TemplateId { get; set; }
        public string? TemplateName { get; set; }
        public string? TemplateDescription { get; set; }
        public int TaskGroupCount { get; set; }
        public string? CurrentTemplateVersion { get; set; }
        public string? TemplateCacheKey { get; set; }
        public bool HasError { get; set; }
        public string? ErrorMessage { get; set; }
        public bool ShowSuccess { get; set; }
        public string? SuccessMessage { get; set; }
        public string? TestToken { get; set; }

        private readonly IFormTemplateProvider _templateProvider;
        private readonly ITemplatesClient _templatesClient;
        private readonly ICacheService<IMemoryCacheType> _cacheService;
        private readonly ILogger<AdminModel> _logger;

        public AdminModel(
            IFormTemplateProvider templateProvider,
            ITemplatesClient templatesClient,
            ICacheService<IMemoryCacheType> cacheService,
            ILogger<AdminModel> logger)
        {
            _templateProvider = templateProvider;
            _templatesClient = templatesClient;
            _cacheService = cacheService;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadTemplateInformationAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostClearAllAsync()
        {
            try
            {
                // Clear all session data
                HttpContext.Session.Clear();
                
                // Clear template cache
                if (!string.IsNullOrEmpty(TemplateCacheKey))
                {
                    _cacheService.Remove(TemplateCacheKey);
                    _logger.LogInformation("Cleared template cache for key: {CacheKey}", TemplateCacheKey);
                }

                ShowSuccess = true;
                SuccessMessage = "Successfully cleared all sessions and caches.";
                
                _logger.LogInformation("Admin cleared all sessions and caches");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear sessions and caches");
                HasError = true;
                ErrorMessage = "Failed to clear sessions and caches. Please try again.";
            }

            await LoadTemplateInformationAsync(true);
            return RedirectToPage("/index");
        }

        public IActionResult OnPostGoToTemplateManager()
        {
            return RedirectToPage("/Admin/TemplateManager");
        }

        private async Task LoadTemplateInformationAsync(bool afterSessionClear = false)
        {
            try
            {
                // retrieve the test token
                TestToken = HttpContext.Session.GetString("TestAuth:Token");

                TemplateId = HttpContext.Session.GetString("TemplateId");

                if (afterSessionClear)
                    return;

                if (string.IsNullOrEmpty(TemplateId))
                {
                    HasError = true;
                    ErrorMessage = "No template ID found in session. Please navigate from the main application.";
                    return;
                }

                // Generate the cache key using the same logic as FormTemplateProvider
                TemplateCacheKey = $"FormTemplate_{CacheKeyHelper.GenerateHashedCacheKey(TemplateId)}";

                // Load template information
                var template = await _templateProvider.GetTemplateAsync(TemplateId);
                if (template != null)
                {
                    TemplateName = template.TemplateName;
                    TemplateDescription = template.Description;
                    TaskGroupCount = template.TaskGroups?.Count ?? 0;
                }

                // Get current template version from API
                var templateResponse = await _templatesClient.GetLatestTemplateSchemaAsync(new Guid(TemplateId));
                CurrentTemplateVersion = templateResponse?.VersionNumber;

                _logger.LogDebug("Loaded admin information for template {TemplateId}", TemplateId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load template information for admin page");
                HasError = true;
                ErrorMessage = "Failed to load template information. Please try again.";
            }
        }

        public string GetSessionKeysInfo()
        {
            var sessionKeys = new List<string>();
            
            // Get common session keys
            var commonKeys = new[]
            {
                "TemplateId",
                "ApplicationId", 
                "ApplicationReference",
                "CurrentAccumulatedApplicationId"
            };

            foreach (var key in commonKeys)
            {
                var value = HttpContext.Session.GetString(key);
                if (!string.IsNullOrEmpty(value))
                {
                    sessionKeys.Add($"{key}: {value}");
                }
            }

            return sessionKeys.Any() ? string.Join(", ", sessionKeys) : "No common session keys found";
        }

        public async Task<string> GetCacheStatusAsync()
        {
            if (string.IsNullOrEmpty(TemplateCacheKey))
            {
                return "Cache key not available";
            }

            try
            {
                // Check if template is cached by trying to get it without calling the factory method
                // We'll use a flag to track if the factory method was called
                var factoryCalled = false;
                
                var cachedTemplate = await _cacheService.GetOrAddAsync<FormTemplate>(
                    TemplateCacheKey,
                    async () =>
                    {
                        factoryCalled = true;
                        // Return null to indicate cache miss without actually caching anything
                        return null!;
                    },
                    nameof(GetCacheStatusAsync));

                // If factory wasn't called, the item was already cached
                // If factory was called, the item wasn't in cache
                return !factoryCalled ? "Template cached" : "Template not in cache";
            }
            catch
            {
                return "Unable to determine cache status";
            }
        }
    }
} 