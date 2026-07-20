using DfE.ExternalApplications.Web.Interfaces;
using HtmlAgilityPack;
using SuperConvert.Extensions;
using System.Dynamic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DfE.ExternalApplications.Web.Services
{
    public class ApplicationCsvGenerator : IApplicationCsvGenerator
    {
        private readonly JsonSerializerOptions serializerOptions = new() { WriteIndented = true };

        public Stream? Generate(string html)
        {
            HtmlDocument doc = new();
            doc.LoadHtml(html);

            IEnumerable<HtmlNode> tasks = doc.DocumentNode.Descendants().Where(x => x.GetDataAttribute("group") != null);

            if (!tasks.Any())
            {
                return null;
            }

            return new MemoryStream();
        }

        public string Generate2(string applicationReference, string applicationData)
        {
            // flatten application response body into a CSV format
            //var csvHeader = "Application reference, starting-year, end-year";
            var csvHeader = string.Empty;

            //var csvData = "app-ref-1, 2027, 2030";
            byte[] csvBytes = applicationData.ToCsv(',');
            //var csvData = Encoding.UTF8.GetString(csvBytes);
            var csvData = string.Empty;
            //var x = new JsonObject(applicationData);
            //JsonObject userObject = new JsonObject
            //{
            //    ["Name"] = "Alice",
            //    ["Age"] = 30,
            //    ["IsActive"] = true
            //};

            List<string> csvHeaders = [];
            List<string> csvItems = [];
            dynamic? obj = JsonSerializer.Deserialize<ExpandoObject>(applicationData);
            if (obj == null) return string.Empty;

            foreach (var kvp in obj)
            {
                //if (csvHeader.Length > 0)
                //{
                //    csvHeader += ", ";
                //}
                //csvHeader += kvp.Key;
                //csvHeaders.Add(kvp.Key);
                // TODO value may be a nested object, so we need to handle that case
                if (kvp.Value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
                {
                    // Handle nested object
                    // nestedKvp[0] is value
                    // nestedKvp[1] is completed
                    //foreach (var nestedKvp in jsonElement.EnumerateObject())
                    //{
                    //    //csvHeaders.Add($"{kvp.Key}.{nestedKvp.Name}");
                    //    csvItems.Add(nestedKvp.Value.ToString());
                    //}
                    JsonElement.ObjectEnumerator nestedObjects = jsonElement.EnumerateObject();
                    JsonElement value = nestedObjects.FirstOrDefault(x => x.Name == "value").Value;
                    JsonElement completed = nestedObjects.FirstOrDefault(x => x.Name == "completed").Value;
                    csvHeaders.Add($"{kvp.Key}.value");
                    csvItems.Add(value.ToString());
                    csvHeaders.Add($"{kvp.Key}.completed");
                    csvItems.Add(completed.ToString());
                }
                else
                {
                    csvHeaders.Add(kvp.Key);
                    csvItems.Add(kvp.Value?.ToString() ?? string.Empty);
                }
            }

            //return $"{csvHeader}{Environment.NewLine}{csvData}";
            csvHeader = string.Join(", ", csvHeaders);
            csvData = string.Join(", ", csvItems);
            return $"{csvHeader}{Environment.NewLine}{csvData}";
        }

        public string? GenerateJson(string html)
        {
            HtmlDocument doc = new();
            doc.LoadHtml(html);

            IEnumerable<HtmlNode> groupNodes = doc.DocumentNode.Descendants().Where(x => x.GetDataAttribute("group") != null);

            if (!groupNodes.Any())
            {
                return null;
            }

            /* Data hierarchy:
            template
            |
            * group
              |
              * task
                |
                * page
                  |
                  * field
            */

            FormTemplateData templateData = new();
            List<FormTemplateData.Group> groups = [];
            foreach (var groupNode in groupNodes) 
            {
                FormTemplateData.Group group = new()
                {
                    Name = groupNode.GetDataAttribute("group").Value,
                    Tasks = groupNode.Descendants().Where(x => x.GetDataAttribute("task") != null).Select(taskNode => new FormTemplateData.Task
                    {
                        Name = taskNode.GetDataAttribute("task").Value,
                        Pages = taskNode.Descendants().Where(x => x.GetDataAttribute("page") != null).Select(pageNode => new FormTemplateData.Page
                        {
                            Name = pageNode.GetDataAttribute("page").Value,
                            Fields = pageNode.Descendants().Where(x => x.GetDataAttribute("field") != null).Select(fieldNode => new FormTemplateData.Field
                            {
                                Name = fieldNode.GetDataAttribute("field").Value,
                                Value = fieldNode.Descendants("dd").SingleOrDefault()?.InnerText.Trim()
                            })
                        })
                    })
                };
                groups.Add(group);
            }
            templateData.Groups = groups;

            return JsonSerializer.Serialize(templateData, serializerOptions);
        }
    }

    public class FormTemplateData
    {
        public IEnumerable<Group>? Groups { get; set; }

        public class Group
        {
            public string? Name { get; set; }
            public IEnumerable<Task>? Tasks { get; set; }
        }

        public class Task
        {
            public string? Name { get; set; }
            public IEnumerable<Page>? Pages { get; set; }
        }

        public class Page
        {
            public string? Name { get; set; }
            public IEnumerable<Field>? Fields { get; set; }
        }

        public class Field
        {
            public string? Name { get; set; }
            public string? Value { get; set; }
        }
    }
}
