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
            string json = File.ReadAllText(@"Services\application-data.json");
            var fields = JsonSerializer.Deserialize<IDictionary<string, object>>(json)!;

            const string appRef = "app-ref-1";
            Csv csv = generator.Generate(appRef, fields);

            Assert.NotNull(csv);
            Assert.True(csv.Count != 0);
            testOutput.WriteLine(csv.Export());

            Assert.Equal(32, csv.Count);
            CsvChecker checker = new(csv, "");
            var index = 0;
            checker.CheckField(index++, "application-reference", appRef);
            checker.CheckField(index++, "starting-year", "2027");
            checker.CheckField(index++, "year-1-workstreams[0].workstream-name", "Workstream 1");
            checker.CheckField(index++, "year-1-workstreams[0].id", "bed844ca-2ece-4f11-9d69-b62831d8dd7a");
            checker.CheckField(index++, "year-1-workstreams[0].workstream-building-blocks-coverage", "Strengthening inclusion across mainstream settings");
            checker.CheckField(index++, "year-1-workstreams[0].workstream-outcomes", "Outcome 1");
            checker.CheckField(index++, "year-1-workstreams[0].workstream-success-measures", "Success 1");
            checker.CheckField(index++, "year-1-workstreams[0].workstream-responsible-lead", "responsible lead 1");
            checker.CheckField(index++, "year-1-workstreams[0].workstream-q2-plan-what-milestones-will-enable-you", "Quarter 2 milestones ");
            checker.CheckField(index++, "year-1-workstreams[0].workstream-q2-plan-where-do-you-expect-your-data-to-be", "Quarter 2 Where data ");
            checker.CheckField(index++, "year-1-workstreams[0].workstream-q2-plan-projected-investment-spend", "Quarter 2 projected investment spend 1");
            checker.CheckField(index++, "year-1-workstreams[0].workstream-q3-plan-what-milestones-will-enable-you", "Quarter 3 milestones 1");
            checker.CheckField(index++, "year-1-workstreams[0].workstream-q3-plan-where-do-you-expect-your-data-to-be", "Quarter 3 Where do you expect your data 1");
            checker.CheckField(index++, "year-1-workstreams[0].workstream-q3-plan-projected-investment-spend", "Quarter 3  projected investment spend 1");
            checker.CheckField(index++, "year-1-workstreams[0].workstream-q4-plan-what-milestones-will-enable-you", "Quarter 4 milestones ");
            checker.CheckField(index++, "year-1-workstreams[0].workstream-q4-plan-where-do-you-expect-your-data-to-be", "Quarter 4 Where do you expect your data 1");
            checker.CheckField(index++, "year-1-workstreams[0].workstream-q4-plan-projected-investment-spend", "Quarter 4 projected investment spend 1");
            checker.CheckField(index++, "year-1-workstreams[1].workstream-name", "Workstream 2");
            checker.CheckField(index++, "year-1-workstreams[1].id", "23c927fe-2a2c-48e9-ac5e-99a5a7084253");
            checker.CheckField(index++, "year-1-workstreams[1].workstream-building-blocks-coverage", "Access to specialist support and placements");
            checker.CheckField(index++, "year-1-workstreams[1].workstream-outcomes", "Outcomes 2");
            checker.CheckField(index++, "year-1-workstreams[1].workstream-success-measures", "Success 2");
            checker.CheckField(index++, "year-1-workstreams[1].workstream-responsible-lead", "responsible lead 2");
            checker.CheckField(index++, "year-1-workstreams[1].workstream-q2-plan-what-milestones-will-enable-you", "Quarter 2 milestones 2");
            checker.CheckField(index++, "year-1-workstreams[1].workstream-q2-plan-where-do-you-expect-your-data-to-be", "Quarter 2 Where do you expect your data 2");
            checker.CheckField(index++, "year-1-workstreams[1].workstream-q2-plan-projected-investment-spend", "Quarter 2 projected investment spend 2");
            checker.CheckField(index++, "year-1-workstreams[1].workstream-q3-plan-what-milestones-will-enable-you", "Quarter 3 milestones 2");
            checker.CheckField(index++, "year-1-workstreams[1].workstream-q3-plan-where-do-you-expect-your-data-to-be", "Quarter 3 Where do you expect your data 2");
            checker.CheckField(index++, "year-1-workstreams[1].workstream-q3-plan-projected-investment-spend", "Quarter 3 projected investment spend 2");
            checker.CheckField(index++, "year-1-workstreams[1].workstream-q4-plan-what-milestones-will-enable-you", "Quarter 4 milestones 2");
            checker.CheckField(index++, "year-1-workstreams[1].workstream-q4-plan-where-do-you-expect-your-data-to-be", "Quarter 4 Where do you expect your data 2");
            checker.CheckField(index++, "year-1-workstreams[1].workstream-q4-plan-projected-investment-spend", "Quarter 4 projected investment spend 2");
        }

        [Fact]
        public void FieldExporterFactory_Create_Simple()
        {
            string json = File.ReadAllText(@"Services\simple-data.json");

            var fields = JsonSerializer.Deserialize<IDictionary<string, object>>(json)!;
            var field = fields.Single();

            FieldExporter fieldExporter = FieldExporterFactory.Create(field.Value.ToString()!);

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
            exporter.Export(field.Key, field.Value.ToString()!, csv);

            testOutput.WriteLine(csv.Export());

            Assert.Equal(1, csv.Count);
            CsvChecker checker = new(csv, "");
            checker.CheckField(0, "field1", "field1 val");
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
            FieldExporter fieldExporter = FieldExporterFactory.Create(json);
            Assert.NotNull(fieldExporter);
            Assert.IsType<ComplexFieldExporter>(fieldExporter);
        }

        [Fact]
        public void ComplexFieldExporter()
        {
            string json = File.ReadAllText(@"Services\complex-data.json");
            Csv csv = new();
            const string fieldName = "test";
            ComplexFieldExporter exporter = new();
            exporter.Export(fieldName, json, csv);

            testOutput.WriteLine($"CSV has {csv.Count} fields");
            testOutput.WriteLine(csv.Export());

            Assert.Equal(3, csv.Count);
            CsvChecker checker = new(csv, fieldName);
            short index = 0;
            checker.CheckField(index++, "[0].field1a", "field1a val");
            checker.CheckField(index++, "[0].field1b", "field1b val");
            checker.CheckField(index++, "[1].field2a", "field2a val");
        }

    }

    internal class CsvChecker(Csv csv, string prefix)
    {
        internal void CheckField(int index, string header, string item)
        {
            var expectedHeader = $"{prefix}{header}";
            var actualHeader = csv.Header(index);
            Assert.Equal(expectedHeader, actualHeader);
            var actualItem = csv.Item(index);
            Assert.Equal(item, actualItem);
        }
    }
}
