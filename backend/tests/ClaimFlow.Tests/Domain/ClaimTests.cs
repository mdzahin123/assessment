using ClaimFlow.Api.Domain;

namespace ClaimFlow.Tests.Domain;

/// <summary>
/// Unit tests for the claim status rules. Pure C#: no database, no web server.
/// A fixed "now" is passed in so results never depend on the real date.
/// </summary>
public class ClaimTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid ClaimantId = Guid.NewGuid();
    private static readonly Guid OtherClaimantId = Guid.NewGuid();
    private static readonly Guid OfficerId = Guid.NewGuid();
    private static readonly Guid OtherOfficerId = Guid.NewGuid();

    // ------------------------------------------------------------------ builders

    private static Policy NewPolicy() => new(Guid.NewGuid(), "MOT-TEST-0001", ClaimantId, ClaimType.Motor, Market.SG,
        50_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

    private static Claim NewSubmittedClaim(decimal claimedAmount = 5_000m) =>
        Claim.Submit(NewPolicy(), ClaimantId, "CLM-TEST", new DateOnly(2026, 6, 10), "Rear-ended at lights", claimedAmount, Now);

    private static Claim NewInReviewClaim()
    {
        var claim = NewSubmittedClaim();
        claim.AssignTo(OfficerId, Now.AddHours(1));
        return claim;
    }

    private static (Claim Claim, Guid RequestId) NewAwaitingInfoClaim()
    {
        var claim = NewInReviewClaim();
        var request = claim.RequestInformation(OfficerId, "Please send photos.", Now.AddHours(2));
        return (claim, request.Id);
    }

    // ------------------------------------------------------------------ Submit

    [Fact]
    public void Submit_creates_claim_in_Submitted_status_with_first_history_entry()
    {
        var claim = NewSubmittedClaim();

        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.Equal(ClaimType.Motor, claim.Type);   // copied from policy
        Assert.Equal(Market.SG, claim.Market);       // copied from policy
        Assert.Null(claim.AssignedOfficerId);
        var entry = Assert.Single(claim.History);
        Assert.Equal(ClaimAction.Submitted, entry.Action);
        Assert.Null(entry.FromStatus);
        Assert.Equal(ClaimantId, entry.ActorId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Submit_rejects_non_positive_amount(decimal amount)
    {
        Assert.Throws<DomainException>(() => NewSubmittedClaim(amount));
    }

    [Fact]
    public void Submit_rejects_amount_with_more_than_two_decimal_places()
    {
        Assert.Throws<DomainException>(() => NewSubmittedClaim(10.555m));
    }

    [Fact]
    public void Submit_rejects_incident_date_in_the_future()
    {
        var tomorrow = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1);
        Assert.Throws<DomainException>(() =>
            Claim.Submit(NewPolicy(), ClaimantId, "CLM-TEST", tomorrow, "x", 100m, Now));
    }

    [Fact]
    public void Submit_rejects_incident_outside_policy_period()
    {
        Assert.Throws<DomainException>(() =>
            Claim.Submit(NewPolicy(), ClaimantId, "CLM-TEST", new DateOnly(2025, 12, 31), "x", 100m, Now));
    }

    [Fact]
    public void Submit_against_someone_elses_policy_is_forbidden()
    {
        Assert.Throws<ForbiddenActionException>(() =>
            Claim.Submit(NewPolicy(), OtherClaimantId, "CLM-TEST", new DateOnly(2026, 6, 10), "x", 100m, Now));
    }

    [Fact]
    public void Submit_rejects_blank_description()
    {
        Assert.Throws<DomainException>(() =>
            Claim.Submit(NewPolicy(), ClaimantId, "CLM-TEST", new DateOnly(2026, 6, 10), "   ", 100m, Now));
    }

    // ------------------------------------------------------------------ AssignTo (pick up)

    [Fact]
    public void AssignTo_moves_Submitted_to_InReview_and_records_officer()
    {
        var claim = NewSubmittedClaim();

        claim.AssignTo(OfficerId, Now.AddHours(1));

        Assert.Equal(ClaimStatus.InReview, claim.Status);
        Assert.Equal(OfficerId, claim.AssignedOfficerId);
        Assert.Equal(ClaimAction.Assigned, claim.History.Last().Action);
    }

    [Fact]
    public void AssignTo_an_already_picked_up_claim_fails_and_keeps_first_officer()
    {
        var claim = NewInReviewClaim(); // already picked up by OfficerId

        Assert.Throws<InvalidStatusTransitionException>(() => claim.AssignTo(OtherOfficerId, Now.AddHours(2)));
        Assert.Equal(OfficerId, claim.AssignedOfficerId);
    }

    // ------------------------------------------------------------------ SetReserve

    [Fact]
    public void SetReserve_updates_amount_without_changing_status()
    {
        var claim = NewInReviewClaim();

        claim.SetReserve(OfficerId, 4_000m, Now.AddHours(2));

        Assert.Equal(4_000m, claim.ReserveAmount);
        Assert.Equal(ClaimStatus.InReview, claim.Status);
        Assert.Equal(ClaimAction.ReserveSet, claim.History.Last().Action);
    }

    [Fact]
    public void SetReserve_by_an_officer_who_is_not_assigned_is_forbidden()
    {
        var claim = NewInReviewClaim();

        Assert.Throws<ForbiddenActionException>(() => claim.SetReserve(OtherOfficerId, 4_000m, Now.AddHours(2)));
        Assert.Null(claim.ReserveAmount);
    }

    [Fact]
    public void SetReserve_on_an_unassigned_claim_fails()
    {
        var claim = NewSubmittedClaim();

        Assert.Throws<InvalidStatusTransitionException>(() => claim.SetReserve(OfficerId, 4_000m, Now.AddHours(1)));
    }

    // ------------------------------------------------------------------ information requests

    [Fact]
    public void RequestInformation_moves_to_AwaitingInfo_and_opens_a_request()
    {
        var (claim, requestId) = NewAwaitingInfoClaim();

        Assert.Equal(ClaimStatus.AwaitingInfo, claim.Status);
        var request = Assert.Single(claim.InformationRequests);
        Assert.Equal(requestId, request.Id);
        Assert.True(request.IsOpen);
    }

    [Fact]
    public void ProvideInformation_answers_the_request_and_returns_claim_to_InReview()
    {
        var (claim, requestId) = NewAwaitingInfoClaim();

        claim.ProvideInformation(ClaimantId, requestId, "Photos attached.", Now.AddHours(3));

        Assert.Equal(ClaimStatus.InReview, claim.Status);
        var request = Assert.Single(claim.InformationRequests);
        Assert.False(request.IsOpen);
        Assert.Equal("Photos attached.", request.Response);
    }

    [Fact]
    public void ProvideInformation_by_a_different_claimant_is_forbidden()
    {
        var (claim, requestId) = NewAwaitingInfoClaim();

        Assert.Throws<ForbiddenActionException>(() =>
            claim.ProvideInformation(OtherClaimantId, requestId, "Not my claim", Now.AddHours(3)));
        Assert.Equal(ClaimStatus.AwaitingInfo, claim.Status);
    }

    [Fact]
    public void ProvideInformation_for_an_unknown_request_fails()
    {
        var (claim, _) = NewAwaitingInfoClaim();

        Assert.Throws<DomainException>(() =>
            claim.ProvideInformation(ClaimantId, Guid.NewGuid(), "Answer", Now.AddHours(3)));
    }

    [Fact]
    public void ProvideInformation_when_nothing_was_requested_fails()
    {
        var claim = NewInReviewClaim();

        Assert.Throws<InvalidStatusTransitionException>(() =>
            claim.ProvideInformation(ClaimantId, Guid.NewGuid(), "Answer", Now.AddHours(2)));
    }

    // ------------------------------------------------------------------ Settle

    [Fact]
    public void Settle_from_InReview_records_amount_and_closes_the_claim()
    {
        var claim = NewInReviewClaim();

        claim.Settle(OfficerId, 4_500.50m, Now.AddHours(2));

        Assert.Equal(ClaimStatus.Settled, claim.Status);
        Assert.Equal(4_500.50m, claim.SettledAmount);
        Assert.True(claim.IsClosed);
        Assert.Equal("Settled for 4500.50", claim.History.Last().Note);
    }

    [Fact]
    public void Settle_while_waiting_for_the_claimant_fails()
    {
        var (claim, _) = NewAwaitingInfoClaim();

        Assert.Throws<InvalidStatusTransitionException>(() => claim.Settle(OfficerId, 1_000m, Now.AddHours(3)));
    }

    [Fact]
    public void Settle_above_the_claimed_amount_fails_and_changes_nothing()
    {
        var claim = NewInReviewClaim(); // claimed 5,000
        var historyCountBefore = claim.History.Count;

        Assert.Throws<DomainException>(() => claim.Settle(OfficerId, 5_000.01m, Now.AddHours(2)));

        Assert.Equal(ClaimStatus.InReview, claim.Status);
        Assert.Null(claim.SettledAmount);
        Assert.Equal(historyCountBefore, claim.History.Count);
    }

    [Fact]
    public void Settle_by_an_officer_who_is_not_assigned_is_forbidden()
    {
        var claim = NewInReviewClaim();

        Assert.Throws<ForbiddenActionException>(() => claim.Settle(OtherOfficerId, 1_000m, Now.AddHours(2)));
        Assert.Equal(ClaimStatus.InReview, claim.Status);
    }

    // ------------------------------------------------------------------ Reject

    [Fact]
    public void Reject_requires_a_reason()
    {
        var claim = NewInReviewClaim();

        Assert.Throws<DomainException>(() => claim.Reject(OfficerId, "", Now.AddHours(2)));
        Assert.Equal(ClaimStatus.InReview, claim.Status);
    }

    [Fact]
    public void Reject_is_allowed_while_waiting_for_the_claimant()
    {
        var (claim, _) = NewAwaitingInfoClaim();

        claim.Reject(OfficerId, "No response from claimant.", Now.AddDays(30));

        Assert.Equal(ClaimStatus.Rejected, claim.Status);
        Assert.Equal("No response from claimant.", claim.RejectionReason);
    }

    // ------------------------------------------------------------------ final states

    [Theory]
    [InlineData(ClaimStatus.Settled)]
    [InlineData(ClaimStatus.Rejected)]
    public void Closed_claims_accept_no_further_actions(ClaimStatus finalStatus)
    {
        var claim = NewInReviewClaim();
        if (finalStatus == ClaimStatus.Settled)
            claim.Settle(OfficerId, 1_000m, Now.AddHours(2));
        else
            claim.Reject(OfficerId, "Excluded", Now.AddHours(2));
        var later = Now.AddDays(1);

        Assert.Throws<InvalidStatusTransitionException>(() => claim.AssignTo(OtherOfficerId, later));
        Assert.Throws<InvalidStatusTransitionException>(() => claim.SetReserve(OfficerId, 100m, later));
        Assert.Throws<InvalidStatusTransitionException>(() => claim.RequestInformation(OfficerId, "?", later));
        Assert.Throws<InvalidStatusTransitionException>(() => claim.ProvideInformation(ClaimantId, Guid.NewGuid(), "!", later));
        Assert.Throws<InvalidStatusTransitionException>(() => claim.Settle(OfficerId, 100m, later));
        Assert.Throws<InvalidStatusTransitionException>(() => claim.Reject(OfficerId, "again", later));
        Assert.Equal(finalStatus, claim.Status);
    }

    // ------------------------------------------------------------------ full journey

    [Fact]
    public void Full_journey_records_every_step_in_order()
    {
        var claim = NewSubmittedClaim();
        claim.AssignTo(OfficerId, Now.AddHours(1));
        claim.SetReserve(OfficerId, 4_000m, Now.AddHours(2));
        var request = claim.RequestInformation(OfficerId, "Police report?", Now.AddHours(3));
        claim.ProvideInformation(ClaimantId, request.Id, "Report ref 123.", Now.AddHours(4));
        claim.Settle(OfficerId, 3_900m, Now.AddHours(5));

        Assert.Equal(
            [ClaimAction.Submitted, ClaimAction.Assigned, ClaimAction.ReserveSet,
             ClaimAction.InformationRequested, ClaimAction.InformationProvided, ClaimAction.Settled],
            claim.History.Select(h => h.Action));
        Assert.Equal(
            [ClaimStatus.Submitted, ClaimStatus.InReview, ClaimStatus.InReview,
             ClaimStatus.AwaitingInfo, ClaimStatus.InReview, ClaimStatus.Settled],
            claim.History.Select(h => h.ToStatus));
        Assert.Equal(Now.AddHours(5), claim.UpdatedAt);
    }
}
