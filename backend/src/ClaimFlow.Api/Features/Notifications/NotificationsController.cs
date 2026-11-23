using ClaimFlow.Api.Infrastructure.Auth;
using ClaimFlow.Api.Infrastructure.Persistence;
using ClaimFlow.Api.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Api.Features.Notifications;

public sealed record NotificationDto(Guid Id, Guid ClaimId, string ClaimNumber, string Message, DateTimeOffset CreatedAt, bool IsRead);

/// <summary>
/// The current user's notifications (claimants and officers). The frontend polls this endpoint:
/// the browser never talks to Kafka directly.
/// </summary>
[ApiController]
[Route("api/me/notifications")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
public class NotificationsController(AppDbContext db, TimeProvider time) : ControllerBase
{
    public const int MaxReturned = 50;

    /// <summary>Most recent notifications first (latest 50: this is a notification bell, not an archive).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<NotificationDto>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<NotificationDto>> List(CancellationToken ct)
    {
        var me = User.ToCurrentUser();
        return await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == me.Id)
            .OrderByDescending(n => n.CreatedAt)
            .Take(MaxReturned)
            .Select(n => new NotificationDto(n.Id, n.ClaimId, n.ClaimNumber, n.Message, n.CreatedAt, n.ReadAt != null))
            .ToListAsync(ct);
    }

    /// <summary>Mark one of MY notifications as read. Someone else's notification is 404.</summary>
    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var me = User.ToCurrentUser();
        var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.UserId == me.Id, ct)
            ?? throw new NotFoundException("Notification not found.");
        notification.MarkRead(time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
