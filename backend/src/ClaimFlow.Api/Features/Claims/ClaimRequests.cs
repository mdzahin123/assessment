using System.ComponentModel.DataAnnotations;
using ClaimFlow.Api.Domain;

namespace ClaimFlow.Api.Features.Claims;

// What the frontend is allowed to SEND. Only the fields listed here can ever reach the domain.
//
// Validation is split on purpose:
//  - Here (attributes): is the request well-formed? Required fields present, text not too long.
//    [ApiController] rejects bad requests with 400 + a list of field errors before our code runs.
//  - Domain (Claim.cs): business rules (amount > 0, max 2 decimals, date within policy...).
// Value types are nullable (Guid?, decimal?) so a MISSING field is reported as "required"
// instead of silently becoming Guid.Empty or 0.

public sealed class SubmitClaimRequest
{
    [Required] public Guid? PolicyId { get; init; }
    [Required] public DateOnly? IncidentDate { get; init; }
    [Required, StringLength(Claim.DescriptionMaxLength)] public string? Description { get; init; }
    [Required] public decimal? ClaimedAmount { get; init; }
}

public sealed class AmountRequest
{
    [Required] public decimal? Amount { get; init; }
}

public sealed class RequestInformationRequest
{
    [Required, StringLength(Claim.TextMaxLength)] public string? Question { get; init; }
}

public sealed class ProvideInformationRequest
{
    [Required, StringLength(Claim.TextMaxLength)] public string? Response { get; init; }
}

public sealed class RejectClaimRequest
{
    [Required, StringLength(Claim.TextMaxLength)] public string? Reason { get; init; }
}

/// <summary>Officer work-queue filter for "who is it assigned to".</summary>
public enum QueueAssignee
{
    All,
    Me,
    Unassigned
}
