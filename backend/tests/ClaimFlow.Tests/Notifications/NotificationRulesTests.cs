using System.Text.Json;
using ClaimFlow.Api.Domain;
using ClaimFlow.Api.Features.Notifications;
using ClaimFlow.Api.Infrastructure.Messaging;

namespace ClaimFlow.Tests.Notifications;

/// <summary>Pure unit tests: who gets notified of what, and the event JSON format. No Kafka, no database.</summary>
public class NotificationRulesTests
{
    private static readonly Guid ClaimantId = Guid.NewGuid();
    private static readonly Guid OfficerId = Guid.NewGuid();

    private static ClaimEvent Event(ClaimAction action, string? note = null, Guid? officer = null) => new(
        ClaimEvent.CurrentSchemaVersion, Guid.NewGuid(), action, Guid.NewGuid(), "CLM-001234", ClaimantId,
        officer ?? OfficerId, Market.SG, ClaimStatus.InReview, ClaimStatus.InReview, OfficerId, note,
        new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(ClaimAction.Submitted, "We have received your claim CLM-001234.")]
    [InlineData(ClaimAction.Assigned, "Your claim CLM-001234 is now being reviewed by a claims officer.")]
    [InlineData(ClaimAction.Settled, "Your claim CLM-001234 has been settled. Settled for 100.00.")]
    public void Claimant_is_told_about_progress_and_decisions(ClaimAction action, string expected)
    {
        var e = Event(action, note: action == ClaimAction.Settled ? "Settled for 100.00" : null);

        var notification = NotificationRules.For(e);

        Assert.NotNull(notification);
        Assert.Equal(ClaimantId, notification.UserId);
        Assert.Equal(expected, notification.Message);
        Assert.Equal(e.EventId, notification.SourceEventId);  // the idempotency key
        Assert.Equal(e.OccurredAt, notification.CreatedAt);   // when it happened, not when it was processed
    }

    [Fact]
    public void Claimant_sees_the_officers_question_and_the_rejection_reason()
    {
        Assert.EndsWith(": Police report?", NotificationRules.For(Event(ClaimAction.InformationRequested, "Police report?"))!.Message);
        Assert.EndsWith("Reason: Excluded", NotificationRules.For(Event(ClaimAction.Rejected, "Excluded"))!.Message);
    }

    [Fact]
    public void Assigned_officer_is_told_when_the_claimant_responds()
    {
        var notification = NotificationRules.For(Event(ClaimAction.InformationProvided));

        Assert.Equal(OfficerId, notification!.UserId);
    }

    [Fact]
    public void Reserve_changes_are_never_sent_to_the_claimant()
    {
        Assert.Null(NotificationRules.For(Event(ClaimAction.ReserveSet, "Reserve set to 9000.00")));
    }

    [Fact]
    public void Outbox_payload_round_trips_with_string_enums_and_claim_id_as_key()
    {
        var e = Event(ClaimAction.Settled, "Settled for 100.00");

        var message = OutboxMessage.For(e);

        Assert.Equal(e.EventId, message.Id);
        Assert.Equal(e.ClaimId.ToString(), message.Key);
        Assert.Contains("\"action\":\"Settled\"", message.Payload);
        Assert.Equal(e, JsonSerializer.Deserialize<ClaimEvent>(message.Payload, EventJson.Options));
    }
}
