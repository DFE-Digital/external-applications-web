using DfE.ExternalApplications.Web.Interfaces;
using DfE.ExternalApplications.Web.Services;

namespace Dfe.ExternalApplications.Web.UnitTests.Services
{
    public class ApplicationImporterTest
    {
        private readonly IApplicationImporter applicationImporter = new ApplicationImporter();

        [Fact]
        public void TestImportApplication()
        {
            // Arrange
            Guid templateId = Guid.Parse("00000000-0000-0000-0000-000000000001");
            using FileStream fileStream = new(@"Services\application.xlsx", FileMode.Open);

            // Act
            ApplicationImportResult result = applicationImporter.ImportSpreadsheet(templateId, fileStream);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Null(result.Errors);
            Assert.Equal(4, result.FieldCount);
        }
    }
}
