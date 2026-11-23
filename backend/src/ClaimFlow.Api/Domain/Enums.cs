namespace ClaimFlow.Api.Domain;

public enum UserRole
{
    Claimant,
    Officer
}

public enum ClaimType
{
    Motor,
    Property
}

/// <summary>
/// The six markets the platform operates in (generic ISO country codes, sample data only).
/// Each market implies its own currency, so money is never summed across markets.
/// </summary>
public enum Market
{
    SG,
    HK,
    AU,
    MY,
    TH,
    ID
}

/// <summary>
/// Allowed flow:
///   Submitted ──AssignTo──► InReview ──RequestInformation──► AwaitingInfo ──ProvideInformation──► InReview
///   InReview ──Settle──► Settled (final)
///   InReview / AwaitingInfo ──Reject──► Rejected (final)
/// </summary>
public enum ClaimStatus
{
    Submitted,
    InReview,
    AwaitingInfo,
    Settled,
    Rejected
}

/// <summary>What happened, for the claim timeline. Not every action changes the status (e.g. ReserveSet).</summary>
public enum ClaimAction
{
    Submitted,
    Assigned,
    ReserveSet,
    InformationRequested,
    InformationProvided,
    Settled,
    Rejected
}
