using DfE.ExternalApplications.Web.Interfaces;
using HtmlAgilityPack;
using System.Diagnostics;
using System.Dynamic;
using System.Text.Json;

namespace DfE.ExternalApplications.Web.Services
{
    public class ApplicationCsvGenerator : IApplicationCsvGenerator
    {
        private readonly JsonSerializerOptions serializerOptions = new() { WriteIndented = true };

        private enum FieldType
        {
            String,
            Object,
            Array
        }

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
            dynamic? obj = JsonSerializer.Deserialize<ExpandoObject>(applicationData);
            if (obj == null) return string.Empty;

            // flatten application response body into a CSV format
            List<string> csvHeaders = [];
            List<string> csvItems = [];

            foreach (var kvp in obj)
            {
                CsvWriter fieldWriter = new(csvHeaders, csvItems, kvp);
                FieldType? fieldType = null;
                if (kvp.Value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
                {
                    JsonElement.ObjectEnumerator nestedObjects = jsonElement.EnumerateObject();
                    JsonElement value = nestedObjects.FirstOrDefault(x => x.Name == "value").Value;
                    var valueString = value.ToString();
                    if (valueString.StartsWith('[') && valueString.EndsWith(']'))
                    {
                        fieldType = FieldType.Array;
                        // TODO parse specific objects - move to own class/method
                        if (valueString.Contains("fileName"))
                        {
                            IEnumerable<JsonElement> objects = JsonSerializer.Deserialize<IEnumerable<JsonElement>>(valueString)!;
                            Debug.WriteLine($"{objects.Count()} file upload objects");
                            foreach (JsonElement obj2 in objects)
                            {
                                JsonElement.ObjectEnumerator nestedObjects2 = obj2.EnumerateObject();
                                JsonElement originalFileName = nestedObjects2.FirstOrDefault(x => x.Name == "originalFileName").Value;
                                JsonElement description = nestedObjects2.FirstOrDefault(x => x.Name == "description").Value;
                                JsonElement fileSize = nestedObjects2.FirstOrDefault(x => x.Name == "fileSize").Value;
                                JsonElement uploadedBy = nestedObjects2.FirstOrDefault(x => x.Name == "uploadedBy").Value;
                                JsonElement uploadedByUser = nestedObjects2.FirstOrDefault(x => x.Name == "uploadedByUser").Value;
                                JsonElement uploadedOn = nestedObjects2.FirstOrDefault(x => x.Name == "uploadedOn").Value;
                                Debug.WriteLine($"{kvp.Key}.value - originalFileName: {originalFileName}, fileSize: {fileSize}.");
                                fieldWriter.AddField("value.originalFileName", originalFileName);
                                fieldWriter.AddField("value.fileSize", fileSize);
                                fieldWriter.AddField("value.description", description);
                                fieldWriter.AddField("value.uploadedByUser", uploadedByUser);
                                fieldWriter.AddField("value.uploadedBy", uploadedBy);
                                fieldWriter.AddField("value.uploadedOn", uploadedOn);
                            }
                        }
                    }
                    else
                    {
                        fieldType = FieldType.Object;
                        csvHeaders.Add($"{kvp.Key}.value");
                        csvItems.Add(valueString);
                    }
                    JsonElement completed = nestedObjects.FirstOrDefault(x => x.Name == "completed").Value;
                    csvHeaders.Add($"{kvp.Key}.completed");
                    csvItems.Add(completed.ToString());
                }
                else
                {
                    csvHeaders.Add(kvp.Key);
                    csvItems.Add(kvp.Value?.ToString() ?? string.Empty);
                }
            }

            var csvHeader = string.Join(", ", csvHeaders);
            var csvData = string.Join(", ", csvItems);
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

    internal class CsvWriter(List<string> csvHeaders, List<string> csvItems, dynamic kvp)
    {
        internal void AddField(string columnName, JsonElement element)
        {
            csvHeaders.Add($"{kvp.Key}.{columnName}");
            csvItems.Add(element.ToString());
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
