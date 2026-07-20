using DfE.ExternalApplications.Web.Services;
using Xunit.Abstractions;

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

        //[Fact]
        //public void Generate()
        //{
        //    string json = @"
        //    {
        //      ""starting-year"": {
        //        ""value"": ""2027"",
        //        ""completed"": true
        //      },
        //      ""end-year"": {
        //        ""value"": ""2030"",
        //        ""completed"": true
        //      }
        //    ";

        //    IEnumerable<string> csv = generator.Generate2("app-ref-1", json);

        //    Assert.True(csv.Any(), "CSV generation failed, result is empty.");
        //    Assert.Equal(2, csv.Count());

        //    string csvHeader = csv.ElementAt(0);
        //    Assert.Equal("Application reference, starting-year, end-year", csvHeader);

        //    string csvData = csv.ElementAt(1);
        //    Assert.Equal("app-ref-1, 2027, 2030", csvData);
        //}


        [Fact]
        public void Generate2()
        {
            //string json = @"
            //  {
            //      ""local-authority-name"": {
            //        ""value"": ""Test Local Authority"",
            //        ""completed"": true
            //      }
            //  },
            //  {
            //        ""integrated-board-name"": {
            //        ""value"": ""Test Integrated Board"",
            //        ""completed"": true
            //      }
            //  }
            //";
            string json = File.ReadAllText(@"Services\application-data.json");

            string csv = generator.Generate2("app-ref-1", json);

            Assert.False(string.IsNullOrEmpty(csv), "CSV generation failed, result is empty.");
            testOutput.WriteLine(csv);

            //Assert.Equal(2, csv.Count());

            //string csvHeader = csv.ElementAt(0);
            //Assert.Equal("Application reference, local-authority-name, integrated-board-name", csvHeader);

            //string csvData = csv.ElementAt(1);
            //Assert.Equal("app-ref-1, Test Local Authority, Test Integrated Board", csvData);

        }
    }
}
