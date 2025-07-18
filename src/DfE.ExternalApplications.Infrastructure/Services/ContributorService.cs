using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Request;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Infrastructure.Services;

/// <summary>
/// Service for managing contributors to applications via external API client
/// </summary>
[ExcludeFromCodeCoverage]
public class ContributorService(
    IApplicationsClient applicationsClient,
    ILogger<ContributorService> logger) : IContributorService
{
    /// <summary>
    /// Gets all contributors for a specific application
    /// </summary>
    public async Task<IReadOnlyList<ContributorDto>> GetApplicationContributorsAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Getting contributors for application {ApplicationId}", applicationId);

            var users = await applicationsClient.GetContributorsAsync(applicationId, includePermissionDetails: false, cancellationToken);
            
            // Convert UserDto to ContributorDto
            var contributors = users?.Select(user => new ContributorDto
            {
                ContributorId = user.UserId,
                EmailAddress = user.Email,
                Name = user.Name,
                Status = "Active", // Default status - could be enhanced based on UserDto properties
                DateInvited = DateTime.UtcNow, // Default date - could be enhanced if available in UserDto
                DateJoined = DateTime.UtcNow
            }).ToList().AsReadOnly() ?? new List<ContributorDto>().AsReadOnly();
            
            logger.LogInformation("Successfully retrieved {Count} contributors for application {ApplicationId}", 
                contributors.Count, applicationId);
            
            return contributors;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting contributors for application {ApplicationId}", applicationId);
            return new List<ContributorDto>().AsReadOnly();
        }
    }

    /// <summary>
    /// Invites a new contributor to an application
    /// </summary>
    public async System.Threading.Tasks.Task InviteContributorAsync(Guid applicationId, InviteContributorRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Inviting contributor {Name} ({Email}) to application {ApplicationId}", request.Name, request.EmailAddress, applicationId);

            // Convert InviteContributorRequest to AddContributorRequest
            var addContributorRequest = new AddContributorRequest
            {
                Email = request.EmailAddress,
                Name = request.Name
            };

            var user = await applicationsClient.AddContributorAsync(applicationId, addContributorRequest, cancellationToken);
            
            logger.LogInformation("Successfully invited contributor {Name} ({Email}) to application {ApplicationId}", 
                request.Name, request.EmailAddress, applicationId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error inviting contributor {Email} to application {ApplicationId}", request.EmailAddress, applicationId);
            throw;
        }
    }

    /// <summary>
    /// Removes a contributor from an application
    /// </summary>
    public async System.Threading.Tasks.Task RemoveContributorAsync(Guid applicationId, Guid contributorId, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Removing contributor {ContributorId} from application {ApplicationId}", contributorId, applicationId);

            // The API method expects userId parameter
            await applicationsClient.RemoveContributorAsync(applicationId, contributorId, cancellationToken);
            
            logger.LogInformation("Successfully removed contributor {ContributorId} from application {ApplicationId}", 
                contributorId, applicationId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing contributor {ContributorId} from application {ApplicationId}", contributorId, applicationId);
            throw;
        }
    }
} 