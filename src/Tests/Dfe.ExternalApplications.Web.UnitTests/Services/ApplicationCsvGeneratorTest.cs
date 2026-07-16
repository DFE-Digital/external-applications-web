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
    }
}
