using System.Text.Json;

namespace ClaimFlow.Api.Infrastructure.Messaging;

/// <summary>
/// A "to send" slip in the outbox tray. Written in the SAME database transaction as the claim change
/// (see AppDbContext.SaveChangesAsync), then delivered to Kafka by <see cref="OutboxPublisher"/>.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; private set; }            // = ClaimEvent.EventId
    public string Type { get; private set; } = null!;
    public string Key { get; private set; } = null!; // Kafka message key = claim ID (keeps one claim's events in order)
    public string Payload { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage For(ClaimEvent claimEvent) => new()
    {
        Id = claimEvent.EventId,
        Type = claimEvent.Action.ToString(),
        Key = claimEvent.ClaimId.ToString(),
        Payload = JsonSerializer.Serialize(claimEvent, EventJson.Options),
        OccurredAt = claimEvent.OccurredAt
    };

    public void MarkPublished(DateTimeOffset now)
    {
        PublishedAt = now;
        Attempts++;
        LastError = null;
    }

    public void RecordFailure(string error)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }
}
