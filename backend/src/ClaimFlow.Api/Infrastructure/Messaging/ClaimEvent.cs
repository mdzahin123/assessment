using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimFlow.Api.Domain;

namespace ClaimFlow.Api.Infrastructure.Messaging;

/// <summary>
/// The message published to Kafka whenever something happens to a claim — the contract between the
/// Claims module (producer) and any consumer (today: Notifications; later e.g. fraud checks, reporting).
///
/// One event per claim timeline entry, so EventId = the ClaimHistoryEntry Id. That gives every event a
/// stable unique ID, which consumers use to ignore duplicates (idempotency).
/// Claim fields (assigned officer etc.) reflect the claim as it was saved.
/// </summary>
public sealed record ClaimEvent(
    int SchemaVersion,
    Guid EventId,
    ClaimAction Action,
    Guid ClaimId,
    string ClaimNumber,
    Guid ClaimantId,
    Guid? AssignedOfficerId,
    Market Market,
    ClaimStatus? FromStatus,
    ClaimStatus ToStatus,
    Guid ActorId,
    string? Note,
    DateTimeOffset OccurredAt)
{
    public const int CurrentSchemaVersion = 1;

    public static ClaimEvent From(Claim claim, ClaimHistoryEntry entry) => new(
        CurrentSchemaVersion,
        entry.Id,
        entry.Action,
        claim.Id,
        claim.ClaimNumber,
        claim.ClaimantId,
        claim.AssignedOfficerId,
        claim.Market,
        entry.FromStatus,
        entry.ToStatus,
        entry.ActorId,
        entry.Note,
        entry.OccurredAt);
}

/// <summary>One JSON format for events on both sides of Kafka (camelCase, enums as strings).</summary>
public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
