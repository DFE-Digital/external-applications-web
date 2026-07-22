using DfE.ExternalApplications.Web.Services;
using System.Dynamic;
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
            JsonElement field = (JsonElement)fields.FirstOrDefault().Value;

            FieldExporter fieldExporter = FieldExporterFactory.Create(field);

            Assert.NotNull(fieldExporter);
            Assert.IsType<SimpleFieldExporter>(fieldExporter);
        }

        [Fact]
        public void FieldExporterFactory_Create_FileUpload()
        {
            var fields = JsonSerializer.Deserialize<IDictionary<string, object>>(@"
              {
                ""upload-blueprint-supporting-evidence"": {
                    ""value"": ""[{\""id\"":\""1f6475d5-7e3e-4f3b-b34c-a543ad49371c\"",\""applicationId\"":\""67708b10-a406-4f67-a5d9-2fa212e7c121\"",\""uploadedBy\"":\""00000000-0000-0000-0000-000000000001\"",\""uploadedByUser\"":null,\""name\"":\""Upload Test.png\"",\""description\"":\""FileDescription\"",\""originalFileName\"":\""Upload Test.png\"",\""fileName\"":\""8876cbe82e813060ee1532e03e5cd900.png\"",\""fileSize\"":7928,\""uploadedOn\"":\""2026-07-09T10:32:53.4880739\""}]"",
                    ""completed"": true
                }
              }")!;
            JsonElement field = (JsonElement)fields.FirstOrDefault().Value;

            FieldExporter fieldExporter = FieldExporterFactory.Create(field);

            Assert.NotNull(fieldExporter);
            Assert.IsType<FileUploadFieldExporter>(fieldExporter);
        }

        [Fact]
        public void SimpleFieldExporter()
        {
            var fields = JsonSerializer.Deserialize<ExpandoObject>(@"
              {
                  ""starting-year"": {
                    ""value"": ""2027"",
                    ""completed"": true
                  }
              }")!;

            // HACK must be a better way to get the first field from an ExpandoObject
            dynamic? field = default;
            foreach (var kvp in fields)
            {
                field = kvp;
                break;
            }

            var fieldValue = (JsonElement)field!.Value;
            JsonElement.ObjectEnumerator nestedObjects = fieldValue.EnumerateObject();
            JsonElement value = nestedObjects.FirstOrDefault(x => x.Name == "value").Value;

            Csv csv = new();

            SimpleFieldExporter exporter = new();
            exporter.Export(field, csv);

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
            var fields = JsonSerializer.Deserialize<ExpandoObject>(@"
              {
                ""upload-blueprint-supporting-evidence"": {
                    ""value"": ""[{\""id\"":\""1f6475d5-7e3e-4f3b-b34c-a543ad49371c\"",\""applicationId\"":\""67708b10-a406-4f67-a5d9-2fa212e7c121\"",\""uploadedBy\"":\""00000000-0000-0000-0000-000000000001\"",\""uploadedByUser\"":null,\""name\"":\""Upload Test.png\"",\""description\"":\""FileDescription\"",\""originalFileName\"":\""Upload Test.png\"",\""fileName\"":\""8876cbe82e813060ee1532e03e5cd900.png\"",\""fileSize\"":7928,\""uploadedOn\"":\""2026-07-09T10:32:53.4880739\""}]"",
                    ""completed"": true
                }
              }")!;

            // HACK must be a better way to get the first field from an ExpandoObject
            dynamic? field = default;
            foreach (var kvp in fields)
            {
                field = kvp;
                break;
            }

            var fieldValue = (JsonElement)field!.Value;
            JsonElement.ObjectEnumerator nestedObjects = fieldValue.EnumerateObject();
            JsonElement value = nestedObjects.FirstOrDefault(x => x.Name == "value").Value;

            Csv csv = new();

            FileUploadFieldExporter exporter = new();
            exporter.Export(field, csv);

            testOutput.WriteLine($"CSV has {csv.Headers.Count} fields");
            testOutput.WriteLine(csv.Export());

            Assert.Equal(6, csv.Headers.Count);
            CsvChecker checker = new(csv, "upload-blueprint-supporting-evidence.value");
            short index = 0;
            checker.CheckField(index++, "originalFileName", "Upload Test.png");
            checker.CheckField(index++, "description", "FileDescription");
            checker.CheckField(index++, "fileSize", "7928");
            checker.CheckField(index++, "uploadedBy", "00000000-0000-0000-0000-000000000001");
            checker.CheckField(index++, "uploadedByUser", "");
            checker.CheckField(index++, "uploadedOn", "2026-07-09T10:32:53.4880739");
        }
    }

    internal class CsvChecker(Csv csv, string prefix)
    {
        internal void CheckField(short index, string header, string item)
        {
            Assert.Equal($"{prefix}.{header}", csv.Headers.ElementAt(index));
            Assert.Equal(item, csv.Items.ElementAt(index));
        }
    }
}
