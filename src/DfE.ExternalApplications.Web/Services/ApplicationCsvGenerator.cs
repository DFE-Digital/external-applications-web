using DfE.ExternalApplications.Web.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Dynamic;
using System.Text.Json;

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
            dynamic? obj = JsonSerializer.Deserialize<ExpandoObject>(applicationData);
            if (obj == null) return string.Empty;

            // flatten application response body into a CSV format
            Csv csv = new();

            foreach (var kvp in obj)
            {
                if (kvp.Value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
                {
                    FieldExporter fieldExporter = FieldExporterFactory.Create(jsonElement);
                    fieldExporter.Export(kvp, csv);
                }
                else
                {
                    // TODO is this the right approach for non-object values? Should we just skip them or handle them differently?
                    csv.AddItem(kvp.Key, kvp.Value?.ToString() ?? string.Empty);
                }
            }

            return csv.Export();
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

        public static class FieldExporterFactory
        {
            private static readonly List<FieldExporter> exporters =
            [
                new ComplexFieldExporter(),
                new FileUploadFieldExporter(),
                new SimpleFieldExporter()
            ];

            public static FieldExporter Create(JsonElement jsonElement)
            {
                return exporters.FirstOrDefault(exporter => exporter.CanExport(jsonElement)) ?? throw new Exception("Unknown field type");
            }
        }

        public abstract class FieldExporter
        {
            public abstract bool CanExport(JsonElement value);

            public abstract void Export(dynamic kvp, Csv csv);
        }

        public class SimpleFieldExporter : FieldExporter
        {
            public override bool CanExport(JsonElement value) => true; // HACK: for now, assume any field that is not a complex or file upload field is a simple field

            public override void Export(dynamic kvp, Csv csv)
            {
                if (kvp.Value is not JsonElement jsonElement || jsonElement.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                JsonElement.ObjectEnumerator nestedObjects = jsonElement.EnumerateObject();
                JsonElement value = nestedObjects.FirstOrDefault(x => x.Name == "value").Value;
                csv.AddItem($"{kvp.Key}.value", value.ToString());
                JsonElement completed = nestedObjects.FirstOrDefault(x => x.Name == "completed").Value;
                csv.AddItem($"{kvp.Key}.completed", completed.ToString());
            }
        }

        public class ComplexFieldExporter : FieldExporter
        {
            public override bool CanExport(JsonElement value)
            {
                // TODO implement logic to determine if this exporter can handle the given JSON structure
                return false;
            }

            public override void Export(dynamic kvp, Csv csv)
            {
                throw new NotImplementedException();
            }
        }

        public class FileUploadFieldExporter : FieldExporter
        {
            public override bool CanExport(JsonElement jsonElement)
            {
                JsonElement.ObjectEnumerator nestedObjects = jsonElement.EnumerateObject();
                JsonElement value = nestedObjects.FirstOrDefault(x => x.Name == "value").Value;
                var valueString = value.ToString();
                if (valueString.StartsWith('[') && valueString.EndsWith(']') && valueString.Contains("fileName"))
                {
                    return true;
                }

                return false;
            }

            public override void Export(dynamic kvp, Csv csv)
            {
                if (kvp.Value is not JsonElement jsonElement || jsonElement.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                JsonElement.ObjectEnumerator nestedObjects = jsonElement.EnumerateObject();
                JsonElement value = nestedObjects.FirstOrDefault(x => x.Name == "value").Value;
                var valueString = value.ToString();
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
                    var prefix = $"{kvp.Key}.value";
                    csv.AddItem($"{prefix}.originalFileName", originalFileName.ToString());
                    csv.AddItem($"{prefix}.description", description.ToString());
                    csv.AddItem($"{prefix}.fileSize", fileSize.ToString());
                    csv.AddItem($"{prefix}.uploadedBy", uploadedBy.ToString());
                    csv.AddItem($"{prefix}.uploadedByUser", uploadedByUser.ToString());
                    csv.AddItem($"{prefix}.uploadedOn", uploadedOn.ToString());
                }
            }
        }

        public class Csv
        {
            public List<string> Headers { get; internal set; } = [];
            public List<string> Items { get; internal set; } = [];

            internal void AddItem(string header, string item)
            {
                Headers.Add(header);
                Items.Add(item);
            }

            public string Export()
            {
                if (Headers.Count != Items.Count) throw new InvalidOperationException("Headers and Items must have the same count.");
                var csvHeader = string.Join(", ", Headers);
                var csvData = string.Join(", ", Items);
                return $"{csvHeader}{Environment.NewLine}{csvData}";
            }
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
