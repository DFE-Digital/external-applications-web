using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Services;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Moq;
using Xunit.Abstractions;

namespace Dfe.ExternalApplications.Web.UnitTests.Services
{
    public class ApplicationImporterTest
    {
        private readonly ITestOutputHelper output;
        private readonly ApplicationImporter applicationImporter;
        private readonly Mock<ITemplateManagementService> mockTemplateManagementService;
        private readonly Mock<IApplicationsClient> mockApplicationsClient;

        public ApplicationImporterTest(ITestOutputHelper output)
        {
            this.output = output;
            mockTemplateManagementService = new Mock<ITemplateManagementService>();
            mockApplicationsClient = new Mock<IApplicationsClient>();
            applicationImporter = new ApplicationImporter(mockTemplateManagementService.Object, mockApplicationsClient.Object);
        }

        [Fact]
        public async System.Threading.Tasks.Task TestImportApplication()
        {
            // Arrange
            var templateId = Guid.Parse("00000000-0000-0000-0000-000000000001");
            using FileStream fileStream = new(@"Services\application.xlsx", FileMode.Open);

            FormTemplate formTemplate = new()
            {
                TemplateId = templateId.ToString()  ,
                TemplateName = "Test Template",
                Description = "Test Description",
                TaskGroups =
                [
                    new() {
                        GroupId = "TG1",
                        GroupName = "Task Group 1",
                        GroupOrder = 1,
                        GroupStatus = "OK",
                        Tasks =
                        [
                            new()
                            {
                                TaskId = "T1",
                                TaskName = "Task 1",
                                TaskOrder = 1,
                                TaskStatusString = "OK",
                                Pages =
                                [
                                    new() {
                                        PageId = "P1",
                                        Description = "Page One",
                                        Slug = "page-1",
                                        Title = "Page 1",
                                        PageOrder = 1,
                                        Fields =
                                        [
                                            new() { FieldId = "application-reference", Order = 1, Type = "string", Label = new Label{ Value = "Field 1" } },
                                            new() { FieldId = "start-year", Order = 2, Type = "string", Label = new Label{ Value = "Field 2" } },
                                            new() { FieldId = "end-year", Order = 3, Type = "string", Label = new Label{ Value = "Field 3" } },
                                            new() { FieldId = "local-authority", Order = 4, Type = "string", Label = new Label{ Value = "Field 4" } }
                                        ]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            };
            mockTemplateManagementService.Setup(s => s.LoadTemplateAsync(templateId.ToString()))
                .ReturnsAsync(formTemplate);

            var applicationId = Guid.Parse("00000000-0000-0000-0000-000000000002");
            mockApplicationsClient.Setup(s => s.CreateApplicationAsync(It.IsAny<CreateApplicationRequest>()))
                .ReturnsAsync(new ApplicationDto { Status = ApplicationStatus.Created, ApplicationId = applicationId });
            mockApplicationsClient.Setup(s => s.SubmitApplicationAsync(applicationId))
                .ReturnsAsync(new ApplicationDto { Status = ApplicationStatus.Submitted });

            // Act
            ApplicationImportResult result = await applicationImporter.ImportSpreadsheet(templateId, fileStream);

            // Assert
            Assert.NotNull(result);
            if (result.Errors != null && result.Errors.Any())
            {
                output.WriteLine($"Errors: {string.Join(", ", result.Errors)}");
            }
            Assert.True(result.Success);
            Assert.Null(result.Errors);
            Assert.Equal(4, result.FieldCount);
        }
    }
}
