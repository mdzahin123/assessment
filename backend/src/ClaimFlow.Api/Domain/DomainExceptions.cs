namespace ClaimFlow.Api.Domain;

// Three kinds of "no" the domain can say. Stage 2 maps each to an HTTP status code
// so the API layer never has to re-implement the business rules.

/// <summary>Input breaks a business rule, e.g. a negative amount. Will map to HTTP 400.</summary>
public class DomainException(string message) : Exception(message);

/// <summary>The action is not allowed in the claim's current status, e.g. settling a rejected claim. Will map to HTTP 409.</summary>
public class InvalidStatusTransitionException(ClaimStatus current, string action)
    : DomainException($"Cannot {action} a claim that is {current}.")
{
    public ClaimStatus CurrentStatus { get; } = current;
}

/// <summary>This person is not allowed to do this to this claim, e.g. an officer who is not assigned. Will map to HTTP 403.</summary>
public class ForbiddenActionException(string message) : DomainException(message);
