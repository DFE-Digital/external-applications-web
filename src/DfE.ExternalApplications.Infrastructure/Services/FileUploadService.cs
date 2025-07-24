using DfE.ExternalApplications.Application.Interfaces;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DfE.ExternalApplications.Infrastructure.Services
{
    public class FileUploadService(IApplicationsClient applicationsClient, ILogger<FileUploadService> logger)
        : IFileUploadService
    {
        public async Task UploadFileAsync(Guid applicationId, string? name = null, string? description = null, FileParameter file = null!, CancellationToken cancellationToken = default)
        {
            try
            {
                logger.LogInformation("Uploading file for application {ApplicationId}", applicationId);
                await applicationsClient.UploadFileAsync(applicationId, name, description, file, cancellationToken);
                logger.LogInformation("File uploaded successfully for application {ApplicationId}", applicationId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error uploading file for application {ApplicationId}", applicationId);
                throw;
            }
        }

        public async Task<IReadOnlyList<UploadDto>> GetFilesForApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
        {
            try
            {
                logger.LogInformation("Getting files for application {ApplicationId}", applicationId);
                var files = await applicationsClient.GetFilesForApplicationAsync(applicationId, cancellationToken);
                return files;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting files for application {ApplicationId}", applicationId);
                return new List<UploadDto>().AsReadOnly();
            }
        }

        public async Task<DownloadFileResult> DownloadFileAsync(Guid fileId, Guid applicationId, CancellationToken cancellationToken = default)
        {
            try
            {
                logger.LogInformation("Downloading file {FileId} for application {ApplicationId}", fileId, applicationId);
                return await applicationsClient.DownloadFileAsync(fileId, applicationId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error downloading file {FileId} for application {ApplicationId}", fileId, applicationId);
                throw;
            }
        }

        public async Task DeleteFileAsync(Guid fileId, Guid applicationId, CancellationToken cancellationToken = default)
        {
            try
            {
                logger.LogInformation("Deleting file {FileId} for application {ApplicationId}", fileId, applicationId);
                await applicationsClient.DeleteFileAsync(fileId, applicationId, cancellationToken);
                logger.LogInformation("File {FileId} deleted for application {ApplicationId}", fileId, applicationId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting file {FileId} for application {ApplicationId}", fileId, applicationId);
                throw;
            }
        }
    }
} 