using DfE.ExternalApplications.Application.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;

namespace DfE.ExternalApplications.Infrastructure.Services;

public class FeedbackService(IUserFeedbackClient client) : IFeedbackService
{
    public async Task<SubmitFeedbackResult> SubmitFeedbackAsync(UserFeedbackRequest request)
    {
        using (AuthenticationContext.UseServiceToServiceAuthScope())
        {
            try
            {
                await client.PostAsync(request);
                return new SubmitFeedbackResult.Success();
            }
            catch (ExternalApplicationsException<ExceptionResponse> e)
            {
                if (e.Result.ExceptionType == "ValidationException")
                {
                    var errors = e.Result.Context?["validationErrors"] as IDictionary<string, string[]>;
                    return new SubmitFeedbackResult.ValidationError(errors ?? new Dictionary<string, string[]>());
                }

                throw;
            }
        }
    }
}