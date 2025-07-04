using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Web.Pages
{
    public class RenderFormModel : PageModel
    {
        public FormTemplate Template { get; set; }
        [BindProperty(SupportsGet = true, Name = "referenceNumber")] public string ReferenceNumber { get; set; }
        [BindProperty] public Dictionary<string, object> Data { get; set; } = new();
        public string TemplateId { get; set; }
        [BindProperty] public string CurrentPageId { get; set; }

        public TaskGroup CurrentGroup { get; set; }
        public Domain.Models.Task CurrentTask { get; set; }
        public Domain.Models.Page CurrentPage { get; set; }

        private readonly IFieldRendererService _renderer;
        private readonly IFormTemplateProvider _templateProvider;
        public RenderFormModel(IFieldRendererService renderer, IFormTemplateProvider templateProvider)
        {
            _renderer = renderer;
            _templateProvider = templateProvider;
        }

        public async Task OnGetAsync(string pageId)
        {
            TemplateId = HttpContext.Session.GetString("TemplateId") ?? string.Empty;
            CurrentPageId = pageId;
            await LoadTemplateAsync();
            InitializeCurrentPage(CurrentPageId);
        }

        public async Task<IActionResult> OnPostPageAsync()
        {
            TemplateId = HttpContext.Session.GetString("TemplateId") ?? string.Empty;
            await LoadTemplateAsync();
            InitializeCurrentPage(CurrentPageId);

            foreach (var key in Request.Form.Keys)
            {

                var match = Regex.Match(key, @"^Data\[(.+?)\]$", RegexOptions.None, TimeSpan.FromMilliseconds(200));

                if (match.Success)
                {
                    var fieldId = match.Groups[1].Value;
                    Data[fieldId] = Request.Form[key];
                }
            }

            ValidatePage(CurrentPage);
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var flatPages = Template.TaskGroups
                .SelectMany(g => g.Tasks).Where(t => t.TaskId == CurrentTask.TaskId)
                .SelectMany(t => t.Pages)
                .OrderBy(p => p.PageOrder)

                .ToList();

            var index = flatPages.FindIndex(p => p.PageId == CurrentPage.PageId);
            if (index >= 0 && index < CurrentTask.Pages.Count - 1)
            {
                var next = flatPages[index + 1];
                return RedirectToPage(new { pageId = next.PageId });
            }

            return Redirect($"~/render-form/{ReferenceNumber}");
        }

        private async Task LoadTemplateAsync()
        {
            Template = await _templateProvider.GetTemplateAsync(TemplateId);
        }

        private void InitializeCurrentPage(string pageId)
        {
            var allPages = Template.TaskGroups
                .SelectMany(g => g.Tasks)
                .SelectMany(t => t.Pages)
                .ToList();

            CurrentPage = allPages.FirstOrDefault(p => p.PageId == pageId) ?? allPages.First();

            var pair = Template.TaskGroups
                .SelectMany(g => g.Tasks.Select(t => new { Group = g, Task = t }))
                .First(x => x.Task.Pages.Contains(CurrentPage));

            CurrentGroup = pair.Group;
            CurrentTask = pair.Task;
        }

        private void ValidatePage(Domain.Models.Page page)
        {
            foreach (var field in page.Fields)
            {
                var key = field.FieldId;
                Data.TryGetValue(key, out var rawValue);
                var value = rawValue?.ToString() ?? string.Empty;

                if (field.Validations == null) continue;

                foreach (var rule in field.Validations)
                {
                    // Conditional application
                    if (rule.Condition != null)
                    {
                        Data.TryGetValue(rule.Condition.TriggerField, out var condRaw);
                        var condVal = condRaw?.ToString();
                        var expected = rule.Condition.Value?.ToString();
                        if (rule.Condition.Operator == "equals" && condVal != expected)
                            continue;
                    }

                    switch (rule.Type)
                    {
                        case "required":
                            if (string.IsNullOrWhiteSpace(value))
                                ModelState.AddModelError(key, rule.Message);
                            break;
                        case "regex":
                            if (!Regex.IsMatch(value, rule.Rule.ToString(), RegexOptions.None, TimeSpan.FromMilliseconds(200)) && !String.IsNullOrWhiteSpace(value))
                                ModelState.AddModelError(key, rule.Message);
                            break;
                        case "maxLength":
                            if (value.Length > int.Parse(rule.Rule.ToString()))
                                ModelState.AddModelError(key, rule.Message);
                            break;
                    }
                }
            }
        }
    }
}
