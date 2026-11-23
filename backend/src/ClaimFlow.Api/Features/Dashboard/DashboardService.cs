using ClaimFlow.Api.Domain;
using ClaimFlow.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Api.Features.Dashboard;

/// <summary>
/// Read-only dashboard queries, computed live from the database on every request (no caching, no Kafka
/// read model): at this scale a query takes milliseconds and the numbers are always current.
/// </summary>
public class DashboardService(AppDbContext db, TimeProvider time)
{
    public async Task<TeamWorkloadDto> GetWorkloadAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var windowStart = now.AddDays(-WorkloadCalculator.DecisionWindowDays);

        var officers = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Officer)
            .Select(u => new WorkloadCalculator.Officer(u.Id, u.Name))
            .ToListAsync(ct);

        // Loads one small row per open claim. Fine for demo volumes; at scale this would become a SQL GROUP BY.
        var openClaims = await db.Claims.AsNoTracking()
            .Where(c => Claim.OpenStatuses.Contains(c.Status))
            .Select(c => new WorkloadCalculator.OpenClaim(c.AssignedOfficerId, c.CreatedAt))
            .ToListAsync(ct);

        // Decisions come from the timeline: it records WHO decided and WHEN.
        var decisions = await db.ClaimHistory.AsNoTracking()
            .Where(h => (h.Action == ClaimAction.Settled || h.Action == ClaimAction.Rejected) && h.OccurredAt >= windowStart)
            .Select(h => new WorkloadCalculator.Decision(h.ActorId, h.Action, h.OccurredAt))
            .ToListAsync(ct);

        return WorkloadCalculator.Build(officers, openClaims, decisions, now);
    }
}
