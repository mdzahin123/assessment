using System.Security.Claims;
using ClaimFlow.Api.Domain;

namespace ClaimFlow.Api.Infrastructure.Auth;

/// <summary>Who is making this request. Passed from controllers into services.</summary>
public sealed record CurrentUser(Guid Id, UserRole Role)
{
    public bool IsOfficer => Role == UserRole.Officer;
}

public static class CurrentUserExtensions
{
    /// <summary>Reads the identity the authentication handler attached to the request.</summary>
    public static CurrentUser ToCurrentUser(this ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Request is not authenticated.");
        var role = principal.FindFirstValue(ClaimTypes.Role)
            ?? throw new InvalidOperationException("Authenticated user has no role.");
        return new CurrentUser(Guid.Parse(id), Enum.Parse<UserRole>(role));
    }
}
