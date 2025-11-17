using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Domain.Events;

/// <summary>
/// Event published when a transfer application is submitted
/// </summary>
[ExcludeFromCodeCoverage]
public record TransferApplicationSubmittedEvent(
    string ApplicationId,
    string ApplicationReference,
    string OutgoingTrustUkprn,
    string OutgoingTrustName,
    bool IsFormAMAT,
    DateTime SubmittedOn,
    List<TransferringAcademy> TransferringAcademies,
    Dictionary<string, object>? Metadata);

/// <summary>
/// Represents an academy being transferred
/// </summary>
[ExcludeFromCodeCoverage]
public record TransferringAcademy(
    string OutgoingAcademyName,
    string OutgoingAcademyUkprn,
    string? IncomingTrustUkprn,
    string IncomingTrustName,
    string? Region,
    string? LocalAuthority,
    Dictionary<string, object>? Metadata);

