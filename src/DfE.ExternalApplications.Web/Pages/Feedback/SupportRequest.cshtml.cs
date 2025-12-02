using DfE.ExternalApplications.Application.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using GovUK.Dfe.ExternalApplications.Api.Client.Settings;
using Microsoft.AspNetCore.Mvc;

namespace DfE.ExternalApplications.Web.Pages.Feedback;

public class SupportRequestModel(
    IApplicationsClient applicationsClient,
    IFeedbackService feedbackService,
    ApiClientSettings apiClientSettings,
    ILogger<FeedbackModel<SupportRequest>> logger)
    : FeedbackModel<SupportRequest>(feedbackService, apiClientSettings, logger)
{
    [BindProperty] public string EmailAddress { get; set; } = null!;

    public IReadOnlyList<string> ApplicationReferences { get; private set; } = [];

    protected override UserFeedbackType UserFeedbackType => UserFeedbackType.SupportRequest;

    protected override SupportRequest BuildUserFeedbackRequest() =>
        new(Message, ReferenceNumber!, EmailAddress, TemplateId);

    protected override async Task FetchFormDataAsync()
    {
        var applications = await applicationsClient.GetMyApplicationsAsync(templateId: TemplateId);
        ApplicationReferences = applications.Select(a => a.ApplicationReference).ToList();

        await base.FetchFormDataAsync();
    }

    protected override void ValidateConditionalProperties()
    {
        if (ReferenceNumber is null)
        {
            ModelState.AddModelError(nameof(ReferenceNumber), "You must choose an option");
        }
    }
}