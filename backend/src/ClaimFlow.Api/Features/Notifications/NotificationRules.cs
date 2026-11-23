using ClaimFlow.Api.Domain;
using ClaimFlow.Api.Infrastructure.Messaging;

namespace ClaimFlow.Api.Features.Notifications;

/// <summary>Who gets told what, for each kind of claim event. Pure function: easy to unit-test.</summary>
public static class NotificationRules
{
    public static Notification? For(ClaimEvent e)
    {
        Notification To(Guid userId, string message) =>
            new(e.EventId, userId, e.ClaimId, e.ClaimNumber, message, e.OccurredAt);

        return e.Action switch
        {
            ClaimAction.Submitted => To(e.ClaimantId, $"We have received your claim {e.ClaimNumber}."),
            ClaimAction.Assigned => To(e.ClaimantId, $"Your claim {e.ClaimNumber} is now being reviewed by a claims officer."),
            ClaimAction.InformationRequested => To(e.ClaimantId, $"We need more information about claim {e.ClaimNumber}: {e.Note}"),
            ClaimAction.InformationProvided => e.AssignedOfficerId is { } officerId
                ? To(officerId, $"The claimant has responded to your question on claim {e.ClaimNumber}.")
                : null,
            ClaimAction.Settled => To(e.ClaimantId, $"Your claim {e.ClaimNumber} has been settled. {e.Note}."),
            ClaimAction.Rejected => To(e.ClaimantId, $"Your claim {e.ClaimNumber} was not approved. Reason: {e.Note}"),
            // ReserveSet is the insurer's internal estimate: never shown to the claimant.
            _ => null
        };
    }
}
