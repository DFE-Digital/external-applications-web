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
        public void Generate_ShouldReturnJson_WhenHtmlIsValid()
        {
            // Arrange
            string html =
                @"<div data-group='group1'>Group 1
                    <div data-task='task1'>Task 1
                        <div data-page='page1'>Page 1
                            <div data-field='field1'>
                                <div>
                                    <dt>field1 label</dt>
                                    <dd>field1 value</dd>
                                </div>
                            </div>
                            <div data-field='field2'>
                                <div>
                                    <dt>field2 label</dt>
                                    <dd>field2 value</dd>
                                </div>
                            </div>
                        </div>
                        <div data-page='page2'>Page 2
                            <div data-field='field3'>Field 3</div>
                        </div>
                    </div>
                </div>
                <div data-group='group2'>Group 2</div>";

            // Act
            string? result = generator.GenerateJson(html);

            // Assert
            Assert.NotNull(result);
            // Additional assertions can be added to verify the content of the stream
            testOutput.WriteLine(result);
        }


        [Fact]
        public void Generate2()
        {
            // TODO test collection field type: year-1-delivery-plan/year-1-workstreams?
            string json = File.ReadAllText(@"Services\application-data.json");

            string csv = generator.Generate2("app-ref-1", json);

            Assert.False(string.IsNullOrEmpty(csv), "CSV generation failed, result is empty.");
            testOutput.WriteLine(csv);

            // TODO check csv fields
        }

        [Fact]
        public void FieldExporterFactory_Create_Simple()
        {
            var fields = JsonSerializer.Deserialize<IDictionary<string, object>>(@"
              {
                  ""starting-year"": {
                    ""value"": ""2027"",
                    ""completed"": true
                  }
              }")!;
            var field = fields.Single();

            FieldExporter fieldExporter = FieldExporterFactory.Create((JsonElement)field.Value);

            Assert.NotNull(fieldExporter);
            Assert.IsType<SimpleFieldExporter>(fieldExporter);
        }

        [Fact]
        public void SimpleFieldExporter()
        {
            var fields = JsonSerializer.Deserialize<IDictionary<string, object>>(@"
              {
                  ""starting-year"": {
                    ""value"": ""2027"",
                    ""completed"": true
                  }
              }")!;
            var field = fields.Single();

            Csv csv = new();

            SimpleFieldExporter exporter = new();
            exporter.Export(fields.Single().Key, (JsonElement)field.Value, csv);

            testOutput.WriteLine(csv.Export());

            Assert.Equal(2, csv.Headers.Count);
            Assert.Equal(2, csv.Items.Count);
            Assert.Equal("starting-year.value", csv.Headers.ElementAt(0));
            Assert.Equal("2027", csv.Items.ElementAt(0));
            Assert.Equal("starting-year.completed", csv.Headers.ElementAt(1));
            Assert.Equal("True", csv.Items.ElementAt(1));
        }

        [Fact]
        public void FileUploadFieldExporter()
        {
            var fields = JsonSerializer.Deserialize<IDictionary<string, object>>(@"
              {
                ""upload-blueprint-supporting-evidence"": {
                    ""value"": ""[{\""id\"":\""1f6475d5-7e3e-4f3b-b34c-a543ad49371c\"",\""applicationId\"":\""67708b10-a406-4f67-a5d9-2fa212e7c121\"",\""uploadedBy\"":\""00000000-0000-0000-0000-000000000001\"",\""uploadedByUser\"":null,\""name\"":\""Upload Test.png\"",\""description\"":\""FileDescription\"",\""originalFileName\"":\""Upload Test.png\"",\""fileName\"":\""8876cbe82e813060ee1532e03e5cd900.png\"",\""fileSize\"":7928,\""uploadedOn\"":\""2026-07-09T10:32:53.4880739\""}]"",
                    ""completed"": true
                }
              }")!;
            var field = fields.Single();

            Csv csv = new();

            ComplexFieldExporter exporter = new();
            exporter.Export(field.Key, (JsonElement)field.Value, csv);

            testOutput.WriteLine($"CSV has {csv.Headers.Count} fields");
            testOutput.WriteLine(csv.Export());

            // TODO value index not required as only one item in array?
            Assert.Equal(11, csv.Headers.Count);
            CsvChecker checker = new(csv, "upload-blueprint-supporting-evidence.value");
            short index = 0;
            checker.CheckField(index++, "[0].id", "1f6475d5-7e3e-4f3b-b34c-a543ad49371c");
            checker.CheckField(index++, "[0].applicationId", "67708b10-a406-4f67-a5d9-2fa212e7c121");
            checker.CheckField(index++, "[0].uploadedBy", "00000000-0000-0000-0000-000000000001");
            checker.CheckField(index++, "[0].uploadedByUser", "");
            checker.CheckField(index++, "[0].name", "Upload Test.png");
            checker.CheckField(index++, "[0].description", "FileDescription");
            checker.CheckField(index++, "[0].originalFileName", "Upload Test.png");
            checker.CheckField(index++, "[0].fileName", "8876cbe82e813060ee1532e03e5cd900.png");
            checker.CheckField(index++, "[0].fileSize", "7928");
            checker.CheckField(index++, "[0].uploadedOn", "2026-07-09T10:32:53.4880739");
            checker = new(csv, "upload-blueprint-supporting-evidence");
            checker.CheckField(10, ".completed", "True");
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
            var fields = JsonSerializer.Deserialize<IDictionary<string, object>>(@"
              {
                  ""year-1-workstreams"": {
                    ""value"": ""[{\""workstream-name\"":\""Workstream 1\"",\""id\"":\""bed844ca-2ece-4f11-9d69-b62831d8dd7a\"",\""workstream-building-blocks-coverage\"":\""Strengthening inclusion across mainstream settings\"",\""workstream-outcomes\"":\""Outcome 1\"",\""workstream-success-measures\"":\""Success 1\"",\""workstream-responsible-lead\"":\""responsible lead 1\"",\""workstream-q2-plan-what-milestones-will-enable-you\"":\""Quarter 2 milestones \"",\""workstream-q2-plan-where-do-you-expect-your-data-to-be\"":\""Quarter 2 Where data \"",\""workstream-q2-plan-projected-investment-spend\"":\""Quarter 2 projected investment spend 1\"",\""workstream-q3-plan-what-milestones-will-enable-you\"":\""Quarter 3 milestones 1\"",\""workstream-q3-plan-where-do-you-expect-your-data-to-be\"":\""Quarter 3 Where do you expect your data 1\"",\""workstream-q3-plan-projected-investment-spend\"":\""Quarter 3  projected investment spend 1\"",\""workstream-q4-plan-what-milestones-will-enable-you\"":\""Quarter 4 milestones \"",\""workstream-q4-plan-where-do-you-expect-your-data-to-be\"":\""Quarter 4 Where do you expect your data 1\"",\""workstream-q4-plan-projected-investment-spend\"":\""Quarter 4 projected investment spend 1\""},{\""workstream-name\"":\""Workstream 2\"",\""id\"":\""23c927fe-2a2c-48e9-ac5e-99a5a7084253\"",\""workstream-building-blocks-coverage\"":\""Access to specialist support and placements\"",\""workstream-outcomes\"":\""Outcomes 2\"",\""workstream-success-measures\"":\""Success 2\"",\""workstream-responsible-lead\"":\""responsible lead 2\"",\""workstream-q2-plan-what-milestones-will-enable-you\"":\""Quarter 2 milestones 2\"",\""workstream-q2-plan-where-do-you-expect-your-data-to-be\"":\""Quarter 2 Where do you expect your data 2\"",\""workstream-q2-plan-projected-investment-spend\"":\""Quarter 2 projected investment spend 2\"",\""workstream-q3-plan-what-milestones-will-enable-you\"":\""Quarter 3 milestones 2\"",\""workstream-q3-plan-where-do-you-expect-your-data-to-be\"":\""Quarter 3 Where do you expect your data 2\"",\""workstream-q3-plan-projected-investment-spend\"":\""Quarter 3 projected investment spend 2\"",\""workstream-q4-plan-what-milestones-will-enable-you\"":\""Quarter 4 milestones 2\"",\""workstream-q4-plan-where-do-you-expect-your-data-to-be\"":\""Quarter 4 Where do you expect your data 2\"",\""workstream-q4-plan-projected-investment-spend\"":\""Quarter 4 projected investment spend 2\""}]"",
                    ""completed"": true
                  }
              }")!;
            var field = fields.Single();

            FieldExporter fieldExporter = FieldExporterFactory.Create((JsonElement)field.Value);

            Assert.NotNull(fieldExporter);
            Assert.IsType<ComplexFieldExporter>(fieldExporter);
        }

        [Fact]
        public void ComplexFieldExporter()
        {
            var fields = JsonSerializer.Deserialize<IDictionary<string, object>>(@"
              {
                  ""year-1-workstreams"": {
                    ""value"": ""[{\""workstream-name\"":\""Workstream 1\"",\""id\"":\""bed844ca-2ece-4f11-9d69-b62831d8dd7a\"",\""workstream-building-blocks-coverage\"":\""Strengthening inclusion across mainstream settings\"",\""workstream-outcomes\"":\""Outcome 1\"",\""workstream-success-measures\"":\""Success 1\"",\""workstream-responsible-lead\"":\""responsible lead 1\"",\""workstream-q2-plan-what-milestones-will-enable-you\"":\""Quarter 2 milestones \"",\""workstream-q2-plan-where-do-you-expect-your-data-to-be\"":\""Quarter 2 Where data \"",\""workstream-q2-plan-projected-investment-spend\"":\""Quarter 2 projected investment spend 1\"",\""workstream-q3-plan-what-milestones-will-enable-you\"":\""Quarter 3 milestones 1\"",\""workstream-q3-plan-where-do-you-expect-your-data-to-be\"":\""Quarter 3 Where do you expect your data 1\"",\""workstream-q3-plan-projected-investment-spend\"":\""Quarter 3  projected investment spend 1\"",\""workstream-q4-plan-what-milestones-will-enable-you\"":\""Quarter 4 milestones \"",\""workstream-q4-plan-where-do-you-expect-your-data-to-be\"":\""Quarter 4 Where do you expect your data 1\"",\""workstream-q4-plan-projected-investment-spend\"":\""Quarter 4 projected investment spend 1\""},{\""workstream-name\"":\""Workstream 2\"",\""id\"":\""23c927fe-2a2c-48e9-ac5e-99a5a7084253\"",\""workstream-building-blocks-coverage\"":\""Access to specialist support and placements\"",\""workstream-outcomes\"":\""Outcomes 2\"",\""workstream-success-measures\"":\""Success 2\"",\""workstream-responsible-lead\"":\""responsible lead 2\"",\""workstream-q2-plan-what-milestones-will-enable-you\"":\""Quarter 2 milestones 2\"",\""workstream-q2-plan-where-do-you-expect-your-data-to-be\"":\""Quarter 2 Where do you expect your data 2\"",\""workstream-q2-plan-projected-investment-spend\"":\""Quarter 2 projected investment spend 2\"",\""workstream-q3-plan-what-milestones-will-enable-you\"":\""Quarter 3 milestones 2\"",\""workstream-q3-plan-where-do-you-expect-your-data-to-be\"":\""Quarter 3 Where do you expect your data 2\"",\""workstream-q3-plan-projected-investment-spend\"":\""Quarter 3 projected investment spend 2\"",\""workstream-q4-plan-what-milestones-will-enable-you\"":\""Quarter 4 milestones 2\"",\""workstream-q4-plan-where-do-you-expect-your-data-to-be\"":\""Quarter 4 Where do you expect your data 2\"",\""workstream-q4-plan-projected-investment-spend\"":\""Quarter 4 projected investment spend 2\""}]"",
                    ""completed"": true
                  }
              }")!;
            var field = fields.Single();

            Csv csv = new();

            ComplexFieldExporter exporter = new();
            exporter.Export(field.Key, (JsonElement)field.Value, csv);

            testOutput.WriteLine($"CSV has {csv.Headers.Count} fields");
            testOutput.WriteLine(csv.Export());

            Assert.Equal(31, csv.Headers.Count);
            CsvChecker checker = new(csv, "year-1-workstreams.value");
            short index = 0;
            checker.CheckField(index++, "[0].workstream-name", "Workstream 1");
            // TODO check all other fields for workstream 1 and workstream 2
            checker = new(csv, "year-1-workstreams");
            checker.CheckField(30, ".completed", "True");

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
