using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Services;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using Moq;
using System.Diagnostics;
using System.Text.Json;
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

            FormTemplate formTemplate = CreateFormTemplate(templateId);

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

        [Fact]
        public async System.Threading.Tasks.Task TestImportApplication2()
        {
            var templateId = Guid.Parse("00000000-0000-0000-0000-000000000001");
            using FileStream fileStream = new(@"Services\application.xlsx", FileMode.Open);

            FormTemplate formTemplate = CreateFormTemplate(templateId);

            mockTemplateManagementService.Setup(s => s.LoadTemplateAsync(templateId.ToString()))
                .ReturnsAsync(formTemplate);

            var applicationId = Guid.Parse("00000000-0000-0000-0000-000000000002");
            mockApplicationsClient.Setup(s => s.CreateApplicationAsync(It.IsAny<CreateApplicationRequest>()))
                .ReturnsAsync(new ApplicationDto { Status = ApplicationStatus.Created, ApplicationId = applicationId });
            mockApplicationsClient.Setup(s => s.SubmitApplicationAsync(applicationId))
                .ReturnsAsync(new ApplicationDto { Status = ApplicationStatus.Submitted });

            Dictionary<string, string> mapping = new()
            {
                { "application-reference", "TaskGroup1/Task1/Page1/application-reference" }
            };
            ApplicationImportResult result = await applicationImporter.ImportSpreadsheet2(templateId, fileStream);

            Assert.NotNull(result);
            if (result.Errors != null && result.Errors.Any())
            {
                output.WriteLine($"Errors: {string.Join(", ", result.Errors)}");
            }
            Assert.True(result.Success);
            Assert.Null(result.Errors);
            Assert.Equal(4, result.FieldCount);
        }

        [Fact]
        public void Temp()
        {
            string responseBodyJson = @"{
              ""starting-year"": {
                ""value"": ""2027"",
                ""completed"": true
              },
              ""end-year"": {
                ""value"": ""2030"",
                ""completed"": true
              },
              ""local-authority-name"": {
                ""value"": ""Test Local Authority"",
                ""completed"": true
              }
            }";
            dynamic? responseBody = JsonSerializer.Deserialize<dynamic>(responseBodyJson);
            Assert.NotNull(responseBody);
            Checker dumper = new(responseBody);
            FieldData startingYear = dumper.Check("starting-year", "2027", true);
            output.WriteLine(JsonSerializer.Serialize(startingYear));
            FieldData endYear = dumper.Check("end-year", "2030", true);
            output.WriteLine(JsonSerializer.Serialize(endYear));
            FieldData localAuthorityName = dumper.Check("local-authority-name", "Test Local Authority", true);
            output.WriteLine(JsonSerializer.Serialize(localAuthorityName));
        }

        private static FormTemplate CreateFormTemplate(Guid templateId)
        {
            return new()
            {
                TemplateId = templateId.ToString(),
                TemplateName = "Test Template",
                Description = "Test Description",
                TaskGroups =
                [
                    new() {
                        GroupId = "TaskGroup1",
                        GroupName = "Task Group 1",
                        GroupOrder = 1,
                        GroupStatus = "OK",
                        Tasks =
                        [
                            new()
                            {
                                TaskId = "Task1",
                                TaskName = "Task 1",
                                TaskOrder = 1,
                                TaskStatusString = "OK",
                                Pages =
                                [
                                    new() {
                                        PageId = "Page1",
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
        }
    }

    internal class Checker(dynamic responseBody)
    {
        internal FieldData Check(string name, string expectedValue, bool expectedCompleted)
        {
            dynamic element = responseBody.GetProperty(name);
            Assert.Equal(expectedValue, element.GetProperty("value").GetString());
            Assert.Equal(expectedCompleted, element.GetProperty("completed").GetBoolean());

            return new FieldData
            {
                FieldId = name,
                Value = expectedValue,
                Completed = expectedCompleted
            };
        }
    }

    internal class FieldData
    {
        public string? FieldId { get; set; }
        public string? Value { get; set; }
        public bool Completed { get; set; }
    }

    public class ApplicationResponseBody
    {
    }
}
