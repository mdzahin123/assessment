namespace ClaimFlow.Api.Domain;

/// <summary>A question an officer asks the claimant. Created and answered only through <see cref="Claim"/>.</summary>
public class InformationRequest
{
    public Guid Id { get; private set; }
    public Guid ClaimId { get; private set; }
    public Guid RequestedById { get; private set; }
    public string Question { get; private set; } = null!;
    public string? Response { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AnsweredAt { get; private set; }

    // Calculated, not stored (EF Core ignores get-only properties without a backing field).
    public bool IsOpen => AnsweredAt is null;

    private InformationRequest() { }

    internal InformationRequest(Guid claimId, Guid requestedById, string question, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        ClaimId = claimId;
        RequestedById = requestedById;
        Question = question;
        CreatedAt = now;
    }

    internal void Answer(string response, DateTimeOffset now)
    {
        if (!IsOpen)
            throw new DomainException("This information request has already been answered.");

        Response = response;
        AnsweredAt = now;
    }
}
