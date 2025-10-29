using Microsoft.AspNetCore.Http;

namespace DfE.ExternalApplications.Application.Interfaces;

/// <summary>
/// Service for cleaning up infected or compromised files from application responses and session storage
/// </summary>
public interface IFileCleanupService
{
    /// <summary>
    /// Removes an infected file from the application response and session storage
    /// </summary>
    /// <param name="applicationId">The application ID containing the file</param>
    /// <param name="fileId">The ID of the file to remove</param>
    /// <param name="fileName">The original file name (used for logging and field lookup)</param>
    /// <param name="session">The HTTP session to clean up</param>
    /// <returns>True if the file was found and removed, false otherwise</returns>
    Task<bool> RemoveInfectedFileAsync(Guid applicationId, Guid fileId, string fileName, ISession session);
}

