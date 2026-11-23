using ClaimFlow.Api.Domain;

namespace ClaimFlow.Api.Features.Claims;

// What the API SENDS back. Shaped for screens, never the raw database entities.
// ReserveAmount (the insurer's internal estimate) is null in every claimant-facing response.

public sealed record UserDto(Guid Id, string Name, string Email, UserRole Role);

public sealed record PolicyDto(
    Guid Id, string PolicyNumber, ClaimType Type, Market Market,
    decimal CoverageLimit, DateOnly ValidFrom, DateOnly ValidTo);

/// <summary>One row in a list (claimant's "my claims" or the officer work queue).</summary>
public sealed record ClaimSummaryDto(
    Guid Id,
    string ClaimNumber,
    ClaimType Type,
    Market Market,
    ClaimStatus Status,
    DateOnly IncidentDate,
    decimal ClaimedAmount,
    decimal? ReserveAmount,
    string ClaimantName,
    Guid? AssignedOfficerId,
    string? AssignedOfficerName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Everything one claim screen needs, including its timeline and information requests.</summary>
public sealed record ClaimDetailDto(
    Guid Id,
    string ClaimNumber,
    string PolicyNumber,
    ClaimType Type,
    Market Market,
    ClaimStatus Status,
    DateOnly IncidentDate,
    string Description,
    decimal ClaimedAmount,
    decimal? ReserveAmount,
    decimal? SettledAmount,
    string? RejectionReason,
    Guid ClaimantId,
    string ClaimantName,
    Guid? AssignedOfficerId,
    string? AssignedOfficerName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ClaimHistoryDto> History,
    IReadOnlyList<InformationRequestDto> InformationRequests);

public sealed record ClaimHistoryDto(
    ClaimAction Action, ClaimStatus? FromStatus, ClaimStatus ToStatus,
    string ActorName, string? Note, DateTimeOffset OccurredAt);

public sealed record InformationRequestDto(
    Guid Id, string Question, string? Response, bool IsOpen,
    DateTimeOffset CreatedAt, DateTimeOffset? AnsweredAt);
