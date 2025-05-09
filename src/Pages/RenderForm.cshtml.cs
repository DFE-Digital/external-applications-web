using DfE.ExternalApplications.Web.Models;
using DfE.ExternalApplications.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DfE.ExternalApplications.Web.Pages
{
    public class RenderFormModel : PageModel
    {
        public FormTemplate Template { get; set; }
        [BindProperty] public Dictionary<string, object> Data { get; set; } = new();
        [BindProperty] public string CurrentPageId { get; set; }

        public TaskGroup CurrentGroup { get; set; }
        public Models.Task CurrentTask { get; set; }
        public Models.Page CurrentPage { get; set; }

        private readonly IFieldRendererService _renderer;
        public RenderFormModel(IFieldRendererService renderer)
        {
            _renderer = renderer;
        }

        public void OnGet(string pageId)
        {
            CurrentPageId = pageId;
            LoadTemplate();
            InitializeCurrentPage(CurrentPageId);
        }

        public async Task<IActionResult> OnPostPageAsync()
        {
            LoadTemplate();
            InitializeCurrentPage(CurrentPageId);

            ValidatePage(CurrentPage);
            if (!ModelState.IsValid)
                return Page();

            // TODO: persist Data

            var flatPages = Template.TaskGroups
                .SelectMany(g => g.Tasks)
                .SelectMany(t => t.Pages)
                .OrderBy(p => p.PageOrder)
                .ToList();
            var idx = flatPages.FindIndex(p => p.PageId == CurrentPage.PageId);
            if (idx >= 0 && idx < flatPages.Count - 1)
            {
                var next = flatPages[idx + 1];
                return RedirectToPage(new { pageId = next.PageId });
            }

            return RedirectToPage("Summary");
        }

        private void LoadTemplate()
        {
            var json = System.IO.File.ReadAllText("templates/form-transfers.json");
            Template = JsonSerializer.Deserialize<FormTemplate>(json);
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

        private void ValidatePage(Models.Page page)
        {
            foreach (var field in page.Fields)
            {
                var key = field.FieldId;
                Data.TryGetValue(key, out var rawValue);
                var value = rawValue?.ToString() ?? string.Empty;

                if (field.Validations == null) continue;

                //foreach (var rule in field.Validations)
                //{
                //    // Conditional application
                //    if (rule.Condition != null)
                //    {
                //        Data.TryGetValue(rule.Condition.TriggerField, out var condRaw);
                //        var condVal = condRaw?.ToString();
                //        var expected = rule.Condition.Value?.ToString();
                //        if (rule.Condition.Operator == "equals" && condVal != expected)
                //            continue;
                //    }

                //    switch (rule.Type)
                //    {
                //        case "required":
                //            if (string.IsNullOrWhiteSpace(value))
                //                ModelState.AddModelError(key, rule.Message);
                //            break;
                //        case "regex":
                //            if (!Regex.IsMatch(value, rule.Rule.ToString()))
                //                ModelState.AddModelError(key, rule.Message);
                //            break;
                //        case "maxLength":
                //            if (value.Length > int.Parse(rule.Rule.ToString()))
                //                ModelState.AddModelError(key, rule.Message);
                //            break;
                //    }
                //}
            }
        }
    }
}
