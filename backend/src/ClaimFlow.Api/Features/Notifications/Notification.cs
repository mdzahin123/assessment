namespace ClaimFlow.Api.Features.Notifications;

/// <summary>
/// A message shown to a user (claimant or officer). Created ONLY from Kafka claim events by the
/// Notifications module, which never reads the Claims tables — so it could become its own service later.
/// </summary>
public class Notification
{
    public Guid Id { get; private set; }

    /// <summary>The claim event this came from. UNIQUE in the database: a re-delivered event can't create a duplicate.</summary>
    public Guid SourceEventId { get; private set; }

    public Guid UserId { get; private set; }
    public Guid ClaimId { get; private set; }
    public string ClaimNumber { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }  // when the event happened, not when we processed it
    public DateTimeOffset? ReadAt { get; private set; }

    private Notification() { }

    public Notification(Guid sourceEventId, Guid userId, Guid claimId, string claimNumber, string message, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        SourceEventId = sourceEventId;
        UserId = userId;
        ClaimId = claimId;
        ClaimNumber = claimNumber;
        Message = message;
        CreatedAt = createdAt;
    }

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now; // reading twice keeps the first time
}
