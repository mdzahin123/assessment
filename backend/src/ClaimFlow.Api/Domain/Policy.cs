namespace ClaimFlow.Api.Domain;

/// <summary>
/// An insurance policy a claimant holds. Policy administration is out of scope:
/// policies only exist as seeded data so claims have something real to point at.
/// </summary>
public class Policy
{
    public Guid Id { get; private set; }
    public string PolicyNumber { get; private set; } = null!;
    public Guid HolderId { get; private set; }
    public User Holder { get; private set; } = null!;
    public ClaimType Type { get; private set; }
    public Market Market { get; private set; }
    public decimal CoverageLimit { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public DateOnly ValidTo { get; private set; }

    private Policy() { }

    public Policy(Guid id, string policyNumber, Guid holderId, ClaimType type, Market market,
        decimal coverageLimit, DateOnly validFrom, DateOnly validTo)
    {
        Id = id;
        PolicyNumber = policyNumber;
        HolderId = holderId;
        Type = type;
        Market = market;
        CoverageLimit = coverageLimit;
        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    public bool Covers(DateOnly date) => date >= ValidFrom && date <= ValidTo;
}
