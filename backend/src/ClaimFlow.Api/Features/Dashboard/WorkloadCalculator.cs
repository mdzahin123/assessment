using ClaimFlow.Api.Domain;

namespace ClaimFlow.Api.Features.Dashboard;

/// <summary>
/// Turns raw facts into the team-workload numbers. Pure function (no database, "now" passed in),
/// so the arithmetic can be unit-tested with exact values.
/// </summary>
public static class WorkloadCalculator
{
    public const int DecisionWindowDays = 7;

    public sealed record Officer(Guid Id, string Name);

    /// <summary>An open claim (Submitted / InReview / AwaitingInfo). Unassigned when AssignedOfficerId is null.</summary>
    public sealed record OpenClaim(Guid? AssignedOfficerId, DateTimeOffset SubmittedAt);

    /// <summary>A settle or reject decision taken from the claim timeline.</summary>
    public sealed record Decision(Guid OfficerId, ClaimAction Action, DateTimeOffset At);

    public static TeamWorkloadDto Build(
        IReadOnlyList<Officer> officers,
        IReadOnlyList<OpenClaim> openClaims,
        IReadOnlyList<Decision> decisions,
        DateTimeOffset now)
    {
        var windowStart = now.AddDays(-DecisionWindowDays);

        var rows = officers
            .Select(officer =>
            {
                var mine = openClaims.Where(c => c.AssignedOfficerId == officer.Id).ToList();
                double? averageAge = mine.Count == 0
                    ? null
                    : Math.Round(mine.Average(c => (now - c.SubmittedAt).TotalDays), 1);

                var recent = decisions.Where(d => d.OfficerId == officer.Id && d.At >= windowStart && d.At <= now).ToList();

                return new OfficerWorkloadDto(
                    officer.Id,
                    officer.Name,
                    mine.Count,
                    averageAge,
                    recent.Count(d => d.Action == ClaimAction.Settled),
                    recent.Count(d => d.Action == ClaimAction.Rejected));
            })
            .OrderByDescending(r => r.OpenClaims)
            .ThenBy(r => r.OfficerName)
            .ToList();

        return new TeamWorkloadDto(
            UnassignedClaims: openClaims.Count(c => c.AssignedOfficerId is null),
            DecisionWindowDays,
            rows,
            now);
    }
}
