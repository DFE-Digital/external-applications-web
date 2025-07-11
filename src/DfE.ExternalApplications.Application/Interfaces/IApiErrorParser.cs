using DfE.ExternalApplications.Domain.Models;

namespace DfE.ExternalApplications.Application.Interfaces;

/// <summary>
/// Provides parsing capabilities for API error responses
/// </summary>
public interface IApiErrorParser
{
    /// <summary>
    /// Parses an exception to extract structured API error information
    /// </summary>
    /// <param name="exception">The exception containing the API error</param>
    /// <returns>Parsed API error information</returns>
    ApiErrorParsingResult ParseApiError(Exception exception);
} 