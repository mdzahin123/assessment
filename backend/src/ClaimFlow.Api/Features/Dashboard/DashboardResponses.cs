namespace ClaimFlow.Api.Features.Dashboard;

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
