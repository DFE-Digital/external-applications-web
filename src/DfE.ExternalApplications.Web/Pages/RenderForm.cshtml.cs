using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using System.Text.RegularExpressions;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Web.Pages
{
    public class RenderFormModel : PageModel
    {
        public FormTemplate Template { get; set; }
        [BindProperty] public Dictionary<string, object> Data { get; set; } = new();
        [BindProperty(SupportsGet = true)] public string TemplateId { get; set; }
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

        public async Task OnGetAsync(string templateId, string pageId)
        {
            TemplateId = templateId;
            CurrentPageId = pageId;
            await LoadTemplateAsync();
            InitializeCurrentPage(CurrentPageId);
        }

        public async Task<IActionResult> OnPostPageAsync()
        {
            await LoadTemplateAsync();
            InitializeCurrentPage(CurrentPageId);

            foreach (var key in Request.Form.Keys)
            {
                Console.WriteLine($"Form Key: {key}, Value: {Request.Form[key]}");
                var match = Regex.Match(key, @"^Data\[(.+?)\]$");

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
                return RedirectToPage(new { templateId = TemplateId, pageId = next.PageId });
            }

            return Redirect($"~/render-form/{TemplateId}");
        }

        private async Task LoadTemplateAsync()
        {
            Template = await _templateProvider.GetTemplateAsync(TemplateId);
        }

        private void LoadTemplate()
        {
            var json = @"
    {
      ""templateId"": ""form-001"",
      ""templateName"": ""Transfer Applications"",
      ""description"": ""A dynamic form for Transfer Applications"",
      ""taskGroups"": [
        {
          ""groupId"": ""group-1"",
          ""groupName"": ""Personal Information"",
          ""groupOrder"": 1,
          ""groupStatus"": ""Incomplete"",
          ""tasks"": [
            {
              ""taskId"": ""task-1"",
              ""taskName"": ""Personal Information"",
              ""taskOrder"": 1,
              ""taskStatus"": ""Incomplete"",
              ""pages"": [
                {
                  ""pageId"": ""page-1"",
                  ""slug"": ""personal-info"",
                  ""title"": ""Personal Information"",
                  ""description"": ""Enter your personal details"",
                  ""pageOrder"": 1,
                  ""fields"": [
                    {
                      ""fieldId"": ""academyName"",
                      ""type"": ""text"",
                      ""label"": {
                        ""value"": ""Academy Name"",
                        ""isPageHeading"": true
                      },
                      ""placeholder"": ""Enter your Academy name here"",
                      ""tooltip"": ""Enter your Academy name"",
                      ""order"": 1,
                      ""visibility"": {
                        ""default"": true
                      },
                      ""validations"": [
                        {
                          ""type"": ""regex"",
                          ""rule"": ""^[a-zA-Z0-9\\s&.,'-]{2,}$"",
                          ""message"": ""Academy name contains invalid characters.""
                        },
                        {
                          ""type"": ""maxLength"",
                          ""rule"": 100,
                          ""message"": ""Academy name cannot exceed 100 characters.""
                        },
                        {
                          ""type"": ""required"",
                          ""rule"": true,
                          ""message"": ""Academy name is required.""
                        }
                      ]
                    },
                    {
                      ""fieldId"": ""academyProfile"",
                      ""type"": ""character-count"",
                      ""label"": {
                        ""value"": ""Academy profile"",
                        ""isPageHeading"": true
                      },
                      ""placeholder"": ""Enter your Academy profile here"",
                      ""tooltip"": ""Academy profile"",
                      ""order"": 2,
                      ""visibility"": {
                        ""default"": true
                      },
                      ""validations"": [
                        {
                          ""type"": ""maxLength"",
                          ""rule"": 100,
                          ""message"": ""Academy profile cannot exceed 100 characters.""
                        },
                        {
                          ""type"": ""required"",
                          ""rule"": true,
                          ""message"": ""Academy profile is required.""
                        }
                      ]
                    },
                    {
                      ""fieldId"": ""gender"",
                      ""type"": ""radios"",
                      ""label"": {
                        ""value"": ""Gender"",
                        ""isPageHeading"": true
                      },
                      ""options"": [
                        {
                          ""value"": ""male"",
                          ""label"": ""Male""
                        },
                        {
                          ""value"": ""female"",
                          ""label"": ""Female""
                        },
                        {
                          ""value"": ""other"",
                          ""label"": ""Other""
                        }
                      ],
                      ""order"": 3,
                      ""visibility"": {
                        ""default"": true
                      },
                      ""validations"": []
                    }
                  ]
                },
                {
                  ""pageId"": ""page-2"",
                  ""slug"": ""personal-info"",
                  ""title"": ""Personal Information"",
                  ""description"": ""Enter your personal details"",
                  ""pageOrder"": 2,
                  ""fields"": [
                    {
                      ""fieldId"": ""age"",
                      ""type"": ""text"",
                      ""label"": {
                        ""value"": ""How old are you?"",
                        ""isPageHeading"": false
                      },
                      ""placeholder"": ""Enter your agee"",
                      ""tooltip"": ""Enter your age"",
                      ""order"": 1,
                      ""visibility"": {
                        ""default"": true
                      }
                    }
                  ]
                }
              ]
            },
            {
              ""taskId"": ""task-2"",
              ""taskName"": ""Employment Information"",
              ""taskOrder"": 2,
              ""taskStatus"": ""Incomplete"",
              ""pages"": [
                {
                  ""pageId"": ""page-3"",
                  ""slug"": ""employment-info"",
                  ""title"": ""Employment Information"",
                  ""description"": ""Employment status and employer details"",
                  ""pageOrder"": 1,
                  ""fields"": [
                    {
                      ""fieldId"": ""employmentStatus"",
                      ""type"": ""select"",
                      ""label"": {
                        ""value"": ""Are you currently employed?""
                      },
                      ""tooltip"": """",
                      ""options"": [
                        {
                          ""value"": ""yes"",
                          ""label"": ""Yes""
                        },
                        {
                          ""value"": ""no"",
                          ""label"": ""No""
                        }
                      ],
                      ""order"": 1,
                      ""visibility"": {
                        ""default"": true
                      }
                    },
                    {
                      ""fieldId"": ""employerName"",
                      ""type"": ""text"",
                      ""tooltip"": ""something"",
                      ""label"": {
                        ""value"": ""Employer's Name""
                      },
                      ""placeholder"": ""Enter your employer's name"",
                      ""order"": 2,
                      ""visibility"": {
                        ""default"": false
                      },
                      ""validations"": [
                        {
                          ""type"": ""required"",
                          ""rule"": true,
                          ""message"": ""Employer's name is required.""
                        }
                      ]
                    }
                  ]
                },
                {
                  ""pageId"": ""page-4"",
                  ""slug"": ""page-to-skip"",
                  ""title"": ""Skip Me if Employed"",
                  ""description"": ""This page may be skipped based on user input"",
                  ""pageOrder"": 2,
                  ""fields"": [
                    {
                      ""fieldId"": ""extraQuestion"",
                      ""type"": ""text"",
                      ""tooltip"": ""something"",
                      ""label"": {
                        ""value"": ""Optional Question""
                      },
                      ""placeholder"": ""Answer if shown"",
                      ""required"": false,
                      ""order"": 1,
                      ""visibility"": {
                        ""default"": true
                      }
                    },
                    {
                      ""fieldId"": ""textArea"",
                      ""type"": ""text-area"",
                      ""tooltip"": ""Something"",
                      ""label"": {
                        ""value"": ""Comments""
                      },
                      ""placeholder"": ""Answer if shown"",
                      ""required"": false,
                      ""order"": 2,
                      ""visibility"": {
                        ""default"": true
                      }
                    }
                  ]
                }
              ]
            }
          ]
        },
        {
          ""groupId"": ""group-2"",
          ""groupName"": ""Academy Information"",
          ""groupOrder"": 2,
          ""groupStatus"": ""Incomplete"",
          ""tasks"": [
            {
              ""taskId"": ""task-3"",
              ""taskName"": ""Final Questions"",
              ""taskOrder"": 3,
              ""taskStatus"": ""Incomplete"",
              ""pages"": [
                {
                  ""pageId"": ""page-4"",
                  ""slug"": ""final-details"",
                  ""title"": ""Additional Information"",
                  ""description"": ""Provide additional details"",
                  ""pageOrder"": 1,
                  ""fields"": []
                }
              ]
            }
          ]
        }
      ]
    }";

            // Deserialize into your FormTemplate model:
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
                            if (!Regex.IsMatch(value, rule.Rule.ToString()) && !String.IsNullOrWhiteSpace(value))
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
