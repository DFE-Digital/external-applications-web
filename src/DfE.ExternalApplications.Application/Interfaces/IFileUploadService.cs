using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;

namespace DfE.ExternalApplications.Application.Interfaces
{
    /// <summary>
    /// Service for managing file uploads for applications
    /// </summary>
    public interface IFileUploadService
    {
        Task UploadFileAsync(Guid applicationId, string? name = null, string? description = null, FileParameter file = null!, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<UploadDto>> GetFilesForApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default);
        Task<FileResponse> DownloadFileAsync(Guid fileId, Guid applicationId, CancellationToken cancellationToken = default);
        Task DeleteFileAsync(Guid fileId, Guid applicationId, CancellationToken cancellationToken = default);
    }
} 