using ClaimFlow.Api.Domain;

namespace ClaimFlow.Api.Features.Dashboard;

// ------------------------------------------------------------------ liability exposure
// Deliberately NO grand total: each market is a different currency, so markets are never added together.

/// <summary>Open claims of one type within one market. Amounts are in the market's currency.</summary>
public sealed record ExposureByTypeDto(
    ClaimType Type,
    int OpenClaims,
    decimal ReservedAmount,         // sum of reserves set so far
    int ClaimsWithoutReserve);      // open but not yet assessed: exposure that isn't quantified yet

/// <summary>One market's exposure, all in that market's currency.</summary>
public sealed record MarketExposureDto(
    Market Market,
    string Currency,
    int OpenClaims,
    decimal ReservedAmount,
    int ClaimsWithoutReserve,
    IReadOnlyList<ExposureByTypeDto> ByType);

public sealed record LiabilityExposureDto(
    IReadOnlyList<MarketExposureDto> Markets,
    DateTimeOffset GeneratedAt);

// ------------------------------------------------------------------ team workload

/// <summary>One officer's row on the Team screen.</summary>
public sealed record OfficerWorkloadDto(
    Guid OfficerId,
    string OfficerName,
    int OpenClaims,                 // assigned to this officer and still open (In review / Awaiting information)
    double? AverageOpenAgeDays,     // average days since those claims were submitted; null if none open
    int SettledLast7Days,
    int RejectedLast7Days);

/// <summary>Team workload: every officer (including those with nothing open) plus the unassigned pile.</summary>
public sealed record TeamWorkloadDto(
    int UnassignedClaims,
    int DecisionWindowDays,
    IReadOnlyList<OfficerWorkloadDto> Officers,
    DateTimeOffset GeneratedAt);
