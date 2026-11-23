using System.Globalization;

namespace ClaimFlow.Api.Domain;

/// <summary>
/// A claim and ALL the rules about how it may change.
///
/// Every setter is private: the only way to change a claim is through the methods below
/// (Submit, AssignTo, SetReserve, RequestInformation, ProvideInformation, Settle, Reject).
/// Each method checks, in this order:
///   1. Is the action allowed in the current status?   -> InvalidStatusTransitionException (409)
///   2. Is this person allowed to do it?               -> ForbiddenActionException (403)
///   3. Is the input valid?                            -> DomainException (400)
/// and then changes the state AND writes a history entry together.
///
/// Role checks ("is this user an officer at all?") are the API layer's job (Stage 2).
/// Time is passed in as <c>now</c> (always UTC) so tests can control the clock.
/// </summary>
public class Claim
{
    public const int DescriptionMaxLength = 2000;
    public const int TextMaxLength = 1000;

    private readonly List<ClaimHistoryEntry> _history = [];
    private readonly List<InformationRequest> _informationRequests = [];

    public Guid Id { get; private set; }
    public string ClaimNumber { get; private set; } = null!;

    public Guid PolicyId { get; private set; }
    public Policy Policy { get; private set; } = null!;
    public Guid ClaimantId { get; private set; }
    public User Claimant { get; private set; } = null!;
    public Guid? AssignedOfficerId { get; private set; }
    public User? AssignedOfficer { get; private set; }

    // Copied from the policy at submission so queue and exposure queries don't need a join.
    public ClaimType Type { get; private set; }
    public Market Market { get; private set; }

    public ClaimStatus Status { get; private set; }

    public DateOnly IncidentDate { get; private set; }
    public string Description { get; private set; } = null!;

    /// <summary>What the claimant asks for.</summary>
    public decimal ClaimedAmount { get; private set; }

    /// <summary>The officer's estimate of what we will actually pay. Sum over open claims = liability exposure.</summary>
    public decimal? ReserveAmount { get; private set; }

    public decimal? SettledAmount { get; private set; }
    public string? RejectionReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Concurrency token, mapped to PostgreSQL's hidden <c>xmin</c> column, which changes on every update.
    /// If two officers save changes based on the same version, the second save fails instead of overwriting.
    /// </summary>
    public uint Version { get; private set; }

    public IReadOnlyCollection<ClaimHistoryEntry> History => _history.AsReadOnly();
    public IReadOnlyCollection<InformationRequest> InformationRequests => _informationRequests.AsReadOnly();

    public bool IsClosed => Status is ClaimStatus.Settled or ClaimStatus.Rejected;

    private Claim() { }

    // ------------------------------------------------------------------ actions

    public static Claim Submit(Policy policy, Guid claimantId, string claimNumber, DateOnly incidentDate,
        string description, decimal claimedAmount, DateTimeOffset now)
    {
        if (policy.HolderId != claimantId)
            throw new ForbiddenActionException("You can only claim against your own policy.");
        if (incidentDate > DateOnly.FromDateTime(now.UtcDateTime))
            throw new DomainException("Incident date cannot be in the future.");
        if (!policy.Covers(incidentDate))
            throw new DomainException("Incident date is outside the policy period.");
        EnsureMoney(claimedAmount, "Claimed amount");
        EnsureText(description, "Description", DescriptionMaxLength);

        var claim = new Claim
        {
            Id = Guid.CreateVersion7(),
            ClaimNumber = claimNumber,
            PolicyId = policy.Id,
            ClaimantId = claimantId,
            Type = policy.Type,
            Market = policy.Market,
            Status = ClaimStatus.Submitted,
            IncidentDate = incidentDate,
            Description = description.Trim(),
            ClaimedAmount = claimedAmount,
            CreatedAt = now,
            UpdatedAt = now
        };
        claim._history.Add(new ClaimHistoryEntry(claim.Id, ClaimAction.Submitted, null, ClaimStatus.Submitted,
            claimantId, null, now));
        return claim;
    }

    /// <summary>An officer picks up an unassigned claim.</summary>
    public void AssignTo(Guid officerId, DateTimeOffset now)
    {
        EnsureStatus("pick up", ClaimStatus.Submitted);

        AssignedOfficerId = officerId;
        Record(ClaimAction.Assigned, ClaimStatus.InReview, officerId, null, now);
    }

    /// <summary>Sets or updates the expected payout. Does not change status.</summary>
    public void SetReserve(Guid officerId, decimal amount, DateTimeOffset now)
    {
        EnsureStatus("set a reserve on", ClaimStatus.InReview, ClaimStatus.AwaitingInfo);
        EnsureAssignedOfficer(officerId);
        EnsureMoney(amount, "Reserve amount");

        ReserveAmount = amount;
        Record(ClaimAction.ReserveSet, Status, officerId, $"Reserve set to {Format(amount)}", now);
    }

    public InformationRequest RequestInformation(Guid officerId, string question, DateTimeOffset now)
    {
        EnsureStatus("request information on", ClaimStatus.InReview);
        EnsureAssignedOfficer(officerId);
        EnsureText(question, "Question", TextMaxLength);

        var request = new InformationRequest(Id, officerId, question.Trim(), now);
        _informationRequests.Add(request);
        Record(ClaimAction.InformationRequested, ClaimStatus.AwaitingInfo, officerId, request.Question, now);
        return request;
    }

    public void ProvideInformation(Guid claimantId, Guid requestId, string response, DateTimeOffset now)
    {
        EnsureStatus("provide information for", ClaimStatus.AwaitingInfo);
        if (claimantId != ClaimantId)
            throw new ForbiddenActionException("Only the claimant can respond to information requests.");
        EnsureText(response, "Response", TextMaxLength);

        var request = _informationRequests.SingleOrDefault(r => r.Id == requestId)
            ?? throw new DomainException("Information request not found on this claim.");
        request.Answer(response.Trim(), now);
        Record(ClaimAction.InformationProvided, ClaimStatus.InReview, claimantId, null, now);
    }

    public void Settle(Guid officerId, decimal amount, DateTimeOffset now)
    {
        EnsureStatus("settle", ClaimStatus.InReview);
        EnsureAssignedOfficer(officerId);
        EnsureMoney(amount, "Settlement amount");
        if (amount > ClaimedAmount)
            throw new DomainException("Settlement amount cannot exceed the claimed amount.");

        SettledAmount = amount;
        Record(ClaimAction.Settled, ClaimStatus.Settled, officerId, $"Settled for {Format(amount)}", now);
    }

    /// <summary>Allowed while awaiting info too (e.g. the claimant never responds).</summary>
    public void Reject(Guid officerId, string reason, DateTimeOffset now)
    {
        EnsureStatus("reject", ClaimStatus.InReview, ClaimStatus.AwaitingInfo);
        EnsureAssignedOfficer(officerId);
        EnsureText(reason, "Rejection reason", TextMaxLength);

        RejectionReason = reason.Trim();
        Record(ClaimAction.Rejected, ClaimStatus.Rejected, officerId, RejectionReason, now);
    }

    // ------------------------------------------------------------------ helpers

    private void Record(ClaimAction action, ClaimStatus newStatus, Guid actorId, string? note, DateTimeOffset now)
    {
        var from = Status;
        Status = newStatus;
        UpdatedAt = now;
        _history.Add(new ClaimHistoryEntry(Id, action, from, newStatus, actorId, note, now));
    }

    private void EnsureStatus(string action, params ClaimStatus[] allowed)
    {
        if (!allowed.Contains(Status))
            throw new InvalidStatusTransitionException(Status, action);
    }

    private void EnsureAssignedOfficer(Guid officerId)
    {
        if (AssignedOfficerId != officerId)
            throw new ForbiddenActionException("Only the officer assigned to this claim can do this.");
    }

    private static void EnsureMoney(decimal amount, string field)
    {
        if (amount <= 0)
            throw new DomainException($"{field} must be greater than zero.");
        if (decimal.Round(amount, 2) != amount)
            throw new DomainException($"{field} cannot have more than 2 decimal places.");
    }

    private static void EnsureText(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{field} is required.");
        if (value.Trim().Length > maxLength)
            throw new DomainException($"{field} cannot be longer than {maxLength} characters.");
    }

    // Invariant culture so "1234.50" never becomes "1234,50" on a machine with a different locale.
    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
