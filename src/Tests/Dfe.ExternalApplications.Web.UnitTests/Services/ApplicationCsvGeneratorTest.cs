using DfE.ExternalApplications.Web.Services;
using System.Text.Json;
using Xunit.Abstractions;
using static DfE.ExternalApplications.Web.Services.ApplicationCsvGenerator;

namespace Dfe.ExternalApplications.Web.UnitTests.Services
{
    public class ApplicationCsvGeneratorTest(ITestOutputHelper testOutput)
    {
        readonly ApplicationCsvGenerator generator = new();

        [Fact]
        public void GenerateCsv()
        {
            // TODO test collection field type: year-1-delivery-plan/year-1-workstreams?
            string json = File.ReadAllText(@"Services\application-data.json");

            string csv = generator.Generate("app-ref-1", json);

            Assert.False(string.IsNullOrEmpty(csv), "CSV generation failed, result is empty.");
            testOutput.WriteLine(csv);

            // TODO check csv fields
        }

        [Fact]
        public void FieldExporterFactory_Create_Simple()
        {
            string json = File.ReadAllText(@"Services\simple-data.json");

            var fields = JsonSerializer.Deserialize<IDictionary<string, object>>(json)!;
            var field = fields.Single();

            FieldExporter fieldExporter = FieldExporterFactory.Create((JsonElement)field.Value);

            Assert.NotNull(fieldExporter);
            Assert.IsType<SimpleFieldExporter>(fieldExporter);
        }

        [Fact]
        public void SimpleFieldExporter()
        {
            Csv csv = new();

            string json = File.ReadAllText(@"Services\simple-data.json");
            var fields = JsonSerializer.Deserialize<IDictionary<string, object>>(json)!;
            var field = fields.Single();

            SimpleFieldExporter exporter = new();
            exporter.Export(fields.Single().Key, (JsonElement)field.Value, csv);

            testOutput.WriteLine(csv.Export());

            Assert.Equal(2, csv.Headers.Count);
            Assert.Equal(2, csv.Items.Count);
            Assert.Equal("field1.value", csv.Headers.ElementAt(0));
            Assert.Equal("field1 val", csv.Items.ElementAt(0));
            Assert.Equal("field1.completed", csv.Headers.ElementAt(1));
            Assert.Equal("True", csv.Items.ElementAt(1));
        }

        /* Defintion of a complex field? For example, file upload, collection flow (workstreams).
        [
	        {
		        "key": "value",
		        ...
	        },
	        ...
        ]
        */

        [Fact]
        public void FieldExporterFactory_Create_Complex()
        {
            string json = File.ReadAllText(@"Services\complex-data.json");
            var fields = JsonSerializer.Deserialize<IDictionary<string, object>>(json)!;
            var field = fields.Single();

            FieldExporter fieldExporter = FieldExporterFactory.Create((JsonElement)field.Value);
            Assert.NotNull(fieldExporter);
            Assert.IsType<ComplexFieldExporter>(fieldExporter);
        }

        [Fact]
        public void ComplexFieldExporter()
        {
            string json = File.ReadAllText(@"Services\complex-data.json");
            IDictionary<string, object> fields = JsonSerializer.Deserialize<IDictionary<string, object>>(json)!;
            Csv csv = new();
            foreach (var field in fields)
            {
                ComplexFieldExporter exporter = new();
                exporter.Export(field.Key, (JsonElement)field.Value, csv);
            }

            testOutput.WriteLine($"CSV has {csv.Headers.Count} fields");
            testOutput.WriteLine(csv.Export());

            Assert.Equal(4, csv.Headers.Count);
            CsvChecker checker = new(csv, "fieldlist");
            short index = 0;
            checker.CheckField(index++, ".value[0].field1a", "field1a val");
            checker.CheckField(index++, ".value[0].field1b", "field1b val");
            checker.CheckField(index++, ".value[1].field2a", "field2a val");
            checker.CheckField(index, ".completed", "True");
        }

    }

    internal class CsvChecker(Csv csv, string prefix)
    {
        internal void CheckField(short index, string header, string item)
        {
            var expectedHeader = $"{prefix}{header}";
            var actualHeader = csv.Headers.ElementAt(index);
            Assert.Equal(expectedHeader, actualHeader);
            var actualItem = csv.Items.ElementAt(index);
            Assert.Equal(item, actualItem);
        }
    }
}
