using DfE.ExternalApplications.Web.Interfaces;
using System.Text.Json;

namespace DfE.ExternalApplications.Web.Services
{
    // TODO move nested classes into separate files, and place all in a subfolder.

    public class ApplicationCsvGenerator : IApplicationCsvGenerator
    {
        public Csv Generate(string appRef, IDictionary<string, object> fields)
        {
            Csv csv = new();
            csv.AddItem("application-reference", appRef);
            foreach (var field in fields)
            {
                var value = field.Value?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                FieldExporter fieldExporter = FieldExporterFactory.Create(value);
                fieldExporter.Export(field.Key, value, csv);
            }
            return csv;
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
                return exporters.Single(x => x.CanExport(element.ToString()));
            }

            public static FieldExporter Create(string value)
            {
                var isJsonObject = value.StartsWith('{') && value.EndsWith('}');
                var isJsonArray = value.StartsWith('[') && value.EndsWith(']');
                if (!isJsonObject && !isJsonArray)
                {
                    return new SimpleFieldExporter();
                }

                JsonElement element = JsonDocument.Parse(value).RootElement;
                return exporters.Single(x => x.CanExport(element.ToString()));
            }
        }

        public abstract class FieldExporter
        {
            public abstract bool CanExport(string field);

            public abstract void Export(string key, string value, Csv csv);
        }

        public class SimpleFieldExporter : FieldExporter
        {
            public override bool CanExport(string field)
            {
                var isJsonObject = field.StartsWith('{') && field.EndsWith('}');
                var isJsonArray = field.StartsWith('[') && field.EndsWith(']');
                return !isJsonObject && !isJsonArray;
            }

            public override void Export(string key, string value, Csv csv)
            {
                csv.AddItem(key, value);
            }
        }

        public class ComplexFieldExporter : FieldExporter
        {
            public override bool CanExport(string field)
            {
                return field.StartsWith('[') && field.EndsWith(']');
            }

            public override void Export(string key, string value, Csv csv)
            {
                JsonElement element = JsonDocument.Parse(value).RootElement;
                if (element.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                JsonElement.ArrayEnumerator items = element.EnumerateArray();
                var index = 0;
                foreach (var item in items)
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    JsonElement.ObjectEnumerator nestedObjects = item.EnumerateObject();
                    foreach (var nested in nestedObjects)
                    {
                        csv.AddItem($"{key}[{index}].{nested.Name}", nested.Value.ToString());
                    }
                    index++;
                }
            }
        }

        public class Csv
        {
            private readonly List<string> headers = [];
            private readonly List<string> items = [];

            public int Count => headers.Count;

            internal void AddItem(string header, string item)
            {
                headers.Add(header);
                items.Add(item);
            }

            public string Export()
            {
                if (headers.Count != items.Count) throw new InvalidOperationException("Headers and Items must have the same count.");
                var csvHeader = string.Join(", ", headers);
                var csvData = string.Join(", ", items);
                return $"{csvHeader}{Environment.NewLine}{csvData}";
            }

            public IEnumerable<char>? Header(int index)
            {
                if (index < 0 || index >= headers.Count) throw new ArgumentOutOfRangeException(nameof(index));
                return headers[index];
            }

            public IEnumerable<char>? Item(int index)
            {
                if (index < 0 || index >= items.Count) throw new ArgumentOutOfRangeException(nameof(index));
                return items[index];
            }
        }
    }
}
