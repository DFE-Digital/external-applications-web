using DfE.ExternalApplications.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace DfE.ExternalApplications.Web.Pages.FormEngine
{
    public class RemoveFieldItemModel(
        IApplicationResponseService applicationResponseService,
        ILogger<RemoveFieldItemModel> logger)
        : PageModel
    {
        private readonly IApplicationResponseService _applicationResponseService = applicationResponseService;
        private readonly ILogger<RemoveFieldItemModel> _logger = logger;

        public async Task<IActionResult> OnPostRemoveFieldItemAsync(string referenceNumber, string taskId, string fieldId, int index)
        {
            if (string.IsNullOrWhiteSpace(fieldId) || index < 0)
            {
                return BadRequest("Field ID and valid index are required");
            }

            // Try to get ApplicationId from session for cleaned marker check
            Guid? applicationId = null;
            var appIdStr = HttpContext.Session.GetString("CurrentAccumulatedApplicationId");
            if (!string.IsNullOrEmpty(appIdStr) && Guid.TryParse(appIdStr, out var appId))
            {
                applicationId = appId;
            }

            var acc = _applicationResponseService.GetAccumulatedFormData(HttpContext.Session, applicationId);
            if (acc.TryGetValue(fieldId, out var existing))
            {
                var json = existing?.ToString() ?? "[]";
                try
                {
                    var list = JsonSerializer.Deserialize<List<object>>(json) ?? new();
                    if (index >= 0 && index < list.Count)
                    {
                        list.RemoveAt(index);
                        var updated = JsonSerializer.Serialize(list);
                        _applicationResponseService.AccumulateFormData(new Dictionary<string, object> { [fieldId] = updated }, HttpContext.Session, applicationId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to remove field item at index {Index} for field {FieldId}", index, fieldId);
                }
            }

            var url = $"/applications/{referenceNumber}/{taskId}";
            return Redirect(url);
        }
    }
}


