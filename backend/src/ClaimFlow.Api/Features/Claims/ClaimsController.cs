using ClaimFlow.Api.Domain;
using ClaimFlow.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimFlow.Api.Features.Claims;

/// <summary>
/// Claim endpoints. Deliberately thin: who is calling → service → result.
/// Errors (400/403/404/409) are produced centrally by ApiExceptionHandler.
/// </summary>
[ApiController]
[Route("api/claims")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public class ClaimsController(ClaimService claims) : ControllerBase
{
    private const string Officer = nameof(UserRole.Officer);
    private const string Claimant = nameof(UserRole.Claimant);

    /// <summary>Claimant reports a new incident against one of their policies.</summary>
    [HttpPost]
    [Authorize(Roles = Claimant)]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ClaimDetailDto>> Submit(SubmitClaimRequest request, CancellationToken ct)
    {
        var claim = await claims.SubmitAsync(User.ToCurrentUser(), request, ct);
        return CreatedAtAction(nameof(Get), new { id = claim.Id }, claim);
    }

    /// <summary>One claim with its timeline. Claimants can only see their own (others return 404).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status200OK)]
    public Task<ClaimDetailDto> Get(Guid id, CancellationToken ct) =>
        claims.GetClaimAsync(User.ToCurrentUser(), id, ct);

    /// <summary>Officer work queue. Defaults to open claims, oldest first.</summary>
    [HttpGet]
    [Authorize(Roles = Officer)]
    [ProducesResponseType<IReadOnlyList<ClaimSummaryDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ClaimSummaryDto>> Queue(
        [FromQuery] ClaimStatus? status, [FromQuery] Market? market,
        [FromQuery] QueueAssignee assignee = QueueAssignee.All, CancellationToken ct = default) =>
        claims.GetQueueAsync(User.ToCurrentUser(), status, market, assignee, ct);

    /// <summary>Officer picks up an unassigned claim (Submitted → InReview).</summary>
    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = Officer)]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status200OK)]
    public Task<ClaimDetailDto> Assign(Guid id, CancellationToken ct) =>
        claims.AssignAsync(User.ToCurrentUser(), id, ct);

    /// <summary>Assigned officer sets the expected payout (does not change status).</summary>
    [HttpPut("{id:guid}/reserve")]
    [Authorize(Roles = Officer)]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status200OK)]
    public Task<ClaimDetailDto> SetReserve(Guid id, AmountRequest request, CancellationToken ct) =>
        claims.SetReserveAsync(User.ToCurrentUser(), id, request.Amount!.Value, ct);

    /// <summary>Assigned officer asks the claimant a question (InReview → AwaitingInfo).</summary>
    [HttpPost("{id:guid}/information-requests")]
    [Authorize(Roles = Officer)]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status200OK)]
    public Task<ClaimDetailDto> RequestInformation(Guid id, RequestInformationRequest request, CancellationToken ct) =>
        claims.RequestInformationAsync(User.ToCurrentUser(), id, request.Question!, ct);

    /// <summary>Claimant answers an open question (AwaitingInfo → InReview).</summary>
    [HttpPost("{id:guid}/information-requests/{requestId:guid}/response")]
    [Authorize(Roles = Claimant)]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status200OK)]
    public Task<ClaimDetailDto> ProvideInformation(Guid id, Guid requestId, ProvideInformationRequest request,
        CancellationToken ct) =>
        claims.ProvideInformationAsync(User.ToCurrentUser(), id, requestId, request.Response!, ct);

    /// <summary>Assigned officer settles the claim (InReview → Settled).</summary>
    [HttpPost("{id:guid}/settle")]
    [Authorize(Roles = Officer)]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status200OK)]
    public Task<ClaimDetailDto> Settle(Guid id, AmountRequest request, CancellationToken ct) =>
        claims.SettleAsync(User.ToCurrentUser(), id, request.Amount!.Value, ct);

    /// <summary>Assigned officer rejects the claim (InReview/AwaitingInfo → Rejected).</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = Officer)]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status200OK)]
    public Task<ClaimDetailDto> Reject(Guid id, RejectClaimRequest request, CancellationToken ct) =>
        claims.RejectAsync(User.ToCurrentUser(), id, request.Reason!, ct);
}
