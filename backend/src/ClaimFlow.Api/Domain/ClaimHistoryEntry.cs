namespace ClaimFlow.Api.Domain;

/// <summary>
/// One line on a claim's timeline. Only created by <see cref="Claim"/> itself,
/// in the same method that changes the claim, so the timeline can never miss a step.
/// </summary>
public class ClaimHistoryEntry
{
    public Guid Id { get; private set; }
    public Guid ClaimId { get; private set; }
    public ClaimAction Action { get; private set; }
    public ClaimStatus? FromStatus { get; private set; } // null on the very first entry (submission)
    public ClaimStatus ToStatus { get; private set; }
    public Guid ActorId { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private ClaimHistoryEntry() { }

    internal ClaimHistoryEntry(Guid claimId, ClaimAction action, ClaimStatus? fromStatus, ClaimStatus toStatus,
        Guid actorId, string? note, DateTimeOffset occurredAt)
    {
        Id = Guid.CreateVersion7();
        ClaimId = claimId;
        Action = action;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ActorId = actorId;
        Note = note;
        OccurredAt = occurredAt;
    }
}
