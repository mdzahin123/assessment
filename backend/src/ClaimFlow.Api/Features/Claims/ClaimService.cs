using System.Linq.Expressions;
using ClaimFlow.Api.Domain;
using ClaimFlow.Api.Infrastructure.Auth;
using ClaimFlow.Api.Infrastructure.Persistence;
using ClaimFlow.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Api.Features.Claims;

/// <summary>
/// Application layer for claims: load → check who may see it → call the domain method → save → map to DTO.
/// Business rules are NOT here; they live in <see cref="Claim"/>. Role checks ("is this an officer?")
/// are on the controller attributes. This class owns one security rule: claimants only see their own claims.
/// </summary>
public class ClaimService(AppDbContext db, TimeProvider time)
{
    // ------------------------------------------------------------------ reads

    public async Task<IReadOnlyList<PolicyDto>> GetMyPoliciesAsync(CurrentUser user, CancellationToken ct) =>
        await db.Policies.AsNoTracking()
            .Where(p => p.HolderId == user.Id)
            .OrderBy(p => p.PolicyNumber)
            .Select(p => new PolicyDto(p.Id, p.PolicyNumber, p.Type, p.Market, p.CoverageLimit, p.ValidFrom, p.ValidTo))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ClaimSummaryDto>> GetMyClaimsAsync(CurrentUser user, CancellationToken ct) =>
        await db.Claims.AsNoTracking()
            .Where(c => c.ClaimantId == user.Id)
            .OrderByDescending(c => c.CreatedAt)
            .Select(ToSummary(includeReserve: false))
            .ToListAsync(ct);

    /// <summary>
    /// Officer work queue, oldest first (fair first-in-first-out). Without a status filter, closed
    /// (Settled/Rejected) claims are hidden because they are no longer work.
    /// No paging yet: fine for demo volumes, listed as a known limitation.
    /// </summary>
    public async Task<IReadOnlyList<ClaimSummaryDto>> GetQueueAsync(CurrentUser officer, ClaimStatus? status,
        Market? market, QueueAssignee assignee, CancellationToken ct)
    {
        var query = db.Claims.AsNoTracking();

        query = status is { } s
            ? query.Where(c => c.Status == s)
            : query.Where(c => Claim.OpenStatuses.Contains(c.Status));
        if (market is { } m)
            query = query.Where(c => c.Market == m);
        query = assignee switch
        {
            QueueAssignee.Me => query.Where(c => c.AssignedOfficerId == officer.Id),
            QueueAssignee.Unassigned => query.Where(c => c.AssignedOfficerId == null),
            _ => query
        };

        return await query.OrderBy(c => c.CreatedAt).Select(ToSummary(includeReserve: true)).ToListAsync(ct);
    }

    public async Task<ClaimDetailDto> GetClaimAsync(CurrentUser user, Guid claimId, CancellationToken ct)
    {
        var claim = await LoadForUserAsync(user, claimId, ct);
        return await ToDetailAsync(claim, user, ct);
    }

    // ------------------------------------------------------------------ actions

    public async Task<ClaimDetailDto> SubmitAsync(CurrentUser claimant, SubmitClaimRequest request, CancellationToken ct)
    {
        var policy = await db.Policies.SingleOrDefaultAsync(p => p.Id == request.PolicyId, ct);
        // Someone else's policy is reported exactly like a non-existent one, so policy IDs can't be probed.
        if (policy is null || policy.HolderId != claimant.Id)
            throw new DomainException("Policy not found.");

        var claim = Claim.Submit(policy, claimant.Id, await NextClaimNumberAsync(ct),
            request.IncidentDate!.Value, request.Description!, request.ClaimedAmount!.Value, time.GetUtcNow());

        db.Claims.Add(claim);
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(claim, claimant, ct);
    }

    public Task<ClaimDetailDto> AssignAsync(CurrentUser officer, Guid claimId, CancellationToken ct) =>
        ActAsync(officer, claimId, (claim, now) => claim.AssignTo(officer.Id, now), ct);

    public Task<ClaimDetailDto> SetReserveAsync(CurrentUser officer, Guid claimId, decimal amount, CancellationToken ct) =>
        ActAsync(officer, claimId, (claim, now) => claim.SetReserve(officer.Id, amount, now), ct);

    public Task<ClaimDetailDto> RequestInformationAsync(CurrentUser officer, Guid claimId, string question, CancellationToken ct) =>
        ActAsync(officer, claimId, (claim, now) => claim.RequestInformation(officer.Id, question, now), ct);

    public Task<ClaimDetailDto> ProvideInformationAsync(CurrentUser claimant, Guid claimId, Guid requestId,
        string response, CancellationToken ct) =>
        ActAsync(claimant, claimId, (claim, now) => claim.ProvideInformation(claimant.Id, requestId, response, now), ct);

    public Task<ClaimDetailDto> SettleAsync(CurrentUser officer, Guid claimId, decimal amount, CancellationToken ct) =>
        ActAsync(officer, claimId, (claim, now) => claim.Settle(officer.Id, amount, now), ct);

    public Task<ClaimDetailDto> RejectAsync(CurrentUser officer, Guid claimId, string reason, CancellationToken ct) =>
        ActAsync(officer, claimId, (claim, now) => claim.Reject(officer.Id, reason, now), ct);

    // ------------------------------------------------------------------ helpers

    /// <summary>Every action follows the same steps; only the domain method differs.</summary>
    private async Task<ClaimDetailDto> ActAsync(CurrentUser user, Guid claimId, Action<Claim, DateTimeOffset> action,
        CancellationToken ct)
    {
        var claim = await LoadForUserAsync(user, claimId, ct);
        action(claim, time.GetUtcNow());   // throws DomainException etc. if a rule is broken -> nothing is saved
        await db.SaveChangesAsync(ct);     // throws DbUpdateConcurrencyException if someone else saved first
        return await ToDetailAsync(claim, user, ct);
    }

    /// <summary>
    /// THE ownership check. A claimant asking for someone else's claim gets "not found" (404),
    /// not "forbidden" (403), so they cannot even learn that the claim exists.
    /// Officers can see every claim (one shared team queue).
    /// </summary>
    private async Task<Claim> LoadForUserAsync(CurrentUser user, Guid claimId, CancellationToken ct)
    {
        var claim = await db.Claims
            .Include(c => c.Policy)
            .Include(c => c.History)
            .Include(c => c.InformationRequests)
            .AsSplitQuery()
            .SingleOrDefaultAsync(c => c.Id == claimId, ct);

        if (claim is null || (!user.IsOfficer && claim.ClaimantId != user.Id))
            throw new NotFoundException("Claim not found.");

        return claim;
    }

    /// <summary>
    /// A PostgreSQL sequence hands out numbers like a deli ticket machine: never the same number twice,
    /// even when two claimants submit at the same instant (unlike "count + 1", which can collide).
    /// Gaps are possible (e.g. a submission that fails validation still used a number); that is normal.
    /// </summary>
    private async Task<string> NextClaimNumberAsync(CancellationToken ct)
    {
        // SqlQueryRaw (not SqlQuery) because SqlQuery would turn {…} into a SQL parameter, which cannot be
        // used inside a quoted name. Safe here: the sequence name is our own constant, never user input.
        var next = await db.Database
            .SqlQueryRaw<long>($"SELECT nextval('\"{AppDbContext.ClaimNumberSequence}\"') AS \"Value\"")
            .SingleAsync(ct);
        return $"CLM-{next:D6}";
    }

    private async Task<ClaimDetailDto> ToDetailAsync(Claim claim, CurrentUser user, CancellationToken ct)
    {
        var history = claim.History
            .Where(h => user.IsOfficer || h.Action != ClaimAction.ReserveSet) // the reserve is internal to the insurer
            .OrderBy(h => h.OccurredAt)
            .ToList();

        // Look names up by ID rather than via navigation properties: right after AssignTo() only the
        // AssignedOfficerId is set, the AssignedOfficer object is not loaded yet.
        var userIds = history.Select(h => h.ActorId).Append(claim.ClaimantId).ToList();
        if (claim.AssignedOfficerId is { } assignedId)
            userIds.Add(assignedId);
        var distinctIds = userIds.Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => distinctIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return new ClaimDetailDto(
            claim.Id,
            claim.ClaimNumber,
            claim.Policy.PolicyNumber,
            claim.Type,
            claim.Market,
            claim.Status,
            claim.IncidentDate,
            claim.Description,
            claim.ClaimedAmount,
            user.IsOfficer ? claim.ReserveAmount : null,
            claim.SettledAmount,
            claim.RejectionReason,
            claim.ClaimantId,
            names[claim.ClaimantId],
            claim.AssignedOfficerId,
            claim.AssignedOfficerId is { } id ? names[id] : null,
            claim.CreatedAt,
            claim.UpdatedAt,
            history.Select(h => new ClaimHistoryDto(h.Action, h.FromStatus, h.ToStatus, names[h.ActorId], h.Note, h.OccurredAt)).ToList(),
            claim.InformationRequests
                .OrderBy(r => r.CreatedAt)
                .Select(r => new InformationRequestDto(r.Id, r.Question, r.Response, r.IsOpen, r.CreatedAt, r.AnsweredAt))
                .ToList());
    }

    private static Expression<Func<Claim, ClaimSummaryDto>> ToSummary(bool includeReserve) =>
        c => new ClaimSummaryDto(
            c.Id, c.ClaimNumber, c.Type, c.Market, c.Status, c.IncidentDate, c.ClaimedAmount,
            includeReserve ? c.ReserveAmount : null,
            c.Claimant.Name,
            c.AssignedOfficerId,
            c.AssignedOfficer != null ? c.AssignedOfficer.Name : null,
            c.CreatedAt, c.UpdatedAt);
}
