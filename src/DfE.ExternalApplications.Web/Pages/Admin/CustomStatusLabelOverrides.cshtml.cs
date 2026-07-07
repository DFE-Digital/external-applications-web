using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Services;
using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Web.Pages.Admin
{
    [ExcludeFromCodeCoverage]
    [Authorize(Roles = "Admin")]
    public class CustomStatusLabelOverridesModel(
        IApplicationStatusService applicationStatusService,
        IFormTemplateProvider formTemplateProvider,
        ICacheService<IMemoryCacheType> cacheService,
        ITemplatesClient templatesClient,
        ILogger<CustomStatusLabelOverridesModel> logger)
        : PageModel
    {
        private readonly ILogger<CustomStatusLabelOverridesModel> _logger = logger;
        private readonly IApplicationStatusService _applicationStatusService = applicationStatusService;
        private readonly ICacheService<IMemoryCacheType> _cacheService = cacheService;
        private readonly IFormTemplateProvider _formTemplateProvider = formTemplateProvider;
        private readonly ITemplatesClient _templatesClient = templatesClient;

        public bool ShowSuccess { get; set; }
        public bool HasError { get; set; }
        public FormTemplate? CurrentTemplate { get; set; }
        public string? CurrentVersionNumber { get; set; }
        [BindProperty]
        public string InProgressOverrideValue { get; set; }
        [BindProperty]
        public string SubmittedOverrideValue { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            bool templateParsed = Guid.TryParse(HttpContext.Session.GetString("TemplateId"), out Guid templateId);

            if (!templateParsed)
            {
                _logger.LogWarning("TemplateId not found in session, or is not a valid Guid");
            }

            var apiResponse = await _templatesClient.GetLatestTemplateSchemaAsync(templateId);
            CurrentVersionNumber = apiResponse.VersionNumber;
            CurrentTemplate = await _formTemplateProvider.GetTemplateAsync(templateId.ToString());
            var statuses = await _applicationStatusService.GetCustomApplicationStatusesAsync(templateId);
            InProgressOverrideValue = _applicationStatusService.GetStatusLabel(ApplicationStatus.InProgress, statuses);
            SubmittedOverrideValue = _applicationStatusService.GetStatusLabel(ApplicationStatus.Submitted, statuses);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            bool templateParsed = Guid.TryParse(HttpContext.Session.GetString("TemplateId"), out Guid templateId);
            if (!templateParsed)
            {
                _logger.LogWarning("TemplateId not found in session during post.");
                return RedirectToPage("/Applications/Dashboard");
            }

            //if (!ValidateInput())
            //{
            //    ShowAddVersionForm = true;
            //    await LoadTemplateDataAsync(templateId);
            //    return Page();
            //}

            List<CustomApplicationStatusDto> customStatuses = new List<CustomApplicationStatusDto>();
            if (InProgressOverrideValue != _applicationStatusService.GetBaseStatusLabel(ApplicationStatus.InProgress))
            {
                await _applicationStatusService.OverrideApplicationStatusLabels(
                    new CustomApplicationStatusDto
                    {
                        Label = InProgressOverrideValue,
                        ApplicationStatus = ApplicationStatus.InProgress,
                        TemplateId = templateId
                    });
                _logger.LogInformation("Successfully overriden in progress application status for {TemplateId}", templateId);
            }

            if (SubmittedOverrideValue != _applicationStatusService.GetBaseStatusLabel(ApplicationStatus.Submitted))
            {
                await _applicationStatusService.OverrideApplicationStatusLabels(
                    new CustomApplicationStatusDto
                    {
                        Label = SubmittedOverrideValue,
                        ApplicationStatus = ApplicationStatus.Submitted,
                        TemplateId = templateId
                    });
                _logger.LogInformation("Successfully overriden submitted application status for {TemplateId}", templateId);
            }
            _cacheService.Remove($"CustomApplicationStatuses_{CacheKeyHelper.GenerateHashedCacheKey(templateId.ToString())}");

            return RedirectToPage(new { success = true });
        }

        public IActionResult OnPostCancelOverride()
        {
            return RedirectToPage();
        }
    }
}
