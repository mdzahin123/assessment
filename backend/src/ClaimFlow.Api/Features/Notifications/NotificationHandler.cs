using ClaimFlow.Api.Infrastructure.Messaging;
using ClaimFlow.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ClaimFlow.Api.Features.Notifications;

/// <summary>
/// Turns one claim event into (at most) one notification — idempotently.
/// Kept separate from the Kafka loop so it can be tested without Kafka.
/// </summary>
public class NotificationHandler(AppDbContext db, ILogger<NotificationHandler> logger)
{
    public async Task HandleAsync(ClaimEvent claimEvent, CancellationToken ct)
    {
        var notification = NotificationRules.For(claimEvent);
        if (notification is null)
            return; // nobody needs to be told about this kind of event

        // Fast path: we've seen this event before (Kafka delivers at-least-once).
        if (await db.Notifications.AnyAsync(n => n.SourceEventId == claimEvent.EventId, ct))
        {
            logger.LogInformation("Duplicate event {EventId} ignored", claimEvent.EventId);
            return;
        }

        db.Notifications.Add(notification);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Safety net: the same event was saved in between our check and our insert.
            // The UNIQUE index on SourceEventId guarantees there is still only one notification.
            logger.LogInformation("Duplicate event {EventId} ignored (unique index)", claimEvent.EventId);
        }
    }
}
