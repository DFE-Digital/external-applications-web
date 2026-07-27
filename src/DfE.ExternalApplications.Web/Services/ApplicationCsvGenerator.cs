using DfE.ExternalApplications.Web.Interfaces;
using System.Diagnostics;
using System.Dynamic;
using System.Text.Json;

namespace DfE.ExternalApplications.Web.Services
{
    public class ApplicationCsvGenerator : IApplicationCsvGenerator
    {
        public string Generate(string applicationReference, string applicationData)
        {
            dynamic? obj = JsonSerializer.Deserialize<ExpandoObject>(applicationData);
            if (obj == null) return string.Empty;

            // flatten application response body into a CSV format
            Csv csv = new();

            foreach (var kvp in obj)
            {
                if (kvp.Value is JsonElement element && element.ValueKind == JsonValueKind.Object)
                {
                    FieldExporter fieldExporter = FieldExporterFactory.Create(element);
                    fieldExporter.Export(kvp.Key, element, csv);
                }
                else
                {
                    // TODO is this the right approach for non-object values? Should we just skip them or handle them differently?
                    csv.AddItem(kvp.Key, kvp.Value?.ToString() ?? string.Empty);
                }
            }

            return csv.Export();
        }

        public static class FieldExporterFactory
        {
            private static readonly List<FieldExporter> exporters =
            [
                new ComplexFieldExporter(),
                new SimpleFieldExporter()
            ];

            public static FieldExporter Create(JsonElement element)
            {
                return exporters.Single(x => x.CanExport(element));
            }
        }

        public abstract class FieldExporter
        {
            public abstract bool CanExport(JsonElement field);

            public abstract void Export(string field, JsonElement element, Csv csv);

            protected bool IsArray(JsonElement field)
            {
                JsonElement.ObjectEnumerator nestedObjects = field.EnumerateObject();
                JsonElement value = nestedObjects.FirstOrDefault(x => x.Name == "value").Value;
                var json = value.ToString();
                return json.StartsWith('[') && json.EndsWith(']');
            }
        }

        public class SimpleFieldExporter : FieldExporter
        {
            public override bool CanExport(JsonElement field)
            {
                return !IsArray(field);
            }

            public override void Export(string field, JsonElement element, Csv csv)
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                JsonElement.ObjectEnumerator nestedObjects = element.EnumerateObject();
                JsonElement value = nestedObjects.FirstOrDefault(x => x.Name == "value").Value;
                csv.AddItem($"{field}.value", value.ToString());
                JsonElement completed = nestedObjects.FirstOrDefault(x => x.Name == "completed").Value;
                csv.AddItem($"{field}.completed", completed.ToString());
            }
        }

        public class ComplexFieldExporter : FieldExporter
        {
            public override bool CanExport(JsonElement field)
            {
                return IsArray(field);
            }

            public override void Export(string field, JsonElement element, Csv csv)
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                JsonElement.ObjectEnumerator nestedObjects = element.EnumerateObject();
                JsonElement value = nestedObjects.FirstOrDefault(x => x.Name == "value").Value;
                var valueString = value.ToString();
                IEnumerable<JsonElement> objects = JsonSerializer.Deserialize<IEnumerable<JsonElement>>(valueString)!;
                Debug.WriteLine($"{objects.Count()} file upload objects");
                foreach (var (obj2, index) in objects.Select((value, i) => (value, i)))
                {
                    JsonElement.ObjectEnumerator nestedObjects2 = obj2.EnumerateObject();
                    foreach (var kvp in nestedObjects2)
                    {
                        var prefix = $"{field}.value[{index}]";
                        csv.AddItem($"{prefix}.{kvp.Name}", kvp.Value.ToString());
                    }
                }
                JsonElement completed = nestedObjects.FirstOrDefault(x => x.Name == "completed").Value;
                csv.AddItem($"{field}.completed", completed.ToString());
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
}
