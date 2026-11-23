using ClaimFlow.Api.Domain;
using ClaimFlow.Api.Features.Claims;
using ClaimFlow.Api.Infrastructure.Auth;
using ClaimFlow.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Api.Features.Users;

/// <summary>
/// Demo user list for the frontend's "log in as" picker. The ONLY public endpoint.
/// (Trivial read, so it uses the DbContext directly instead of adding a service layer.)
/// </summary>
[ApiController]
[Route("api/users")]
[AllowAnonymous]
public class UsersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<UserDto>> List(CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .OrderBy(u => u.Role).ThenBy(u => u.Name)
            .Select(u => new UserDto(u.Id, u.Name, u.Email, u.Role))
            .ToListAsync(ct);
}

/// <summary>Things about the person currently making the request.</summary>
[ApiController]
[Route("api/me")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class MeController(AppDbContext db, ClaimService claims) : ControllerBase
{
    [HttpGet]
    public async Task<UserDto> Get(CancellationToken ct)
    {
        var me = User.ToCurrentUser();
        return await db.Users.AsNoTracking()
            .Where(u => u.Id == me.Id)
            .Select(u => new UserDto(u.Id, u.Name, u.Email, u.Role))
            .SingleAsync(ct);
    }

    /// <summary>Policies the claimant can claim against.</summary>
    [HttpGet("policies")]
    [Authorize(Roles = nameof(UserRole.Claimant))]
    public Task<IReadOnlyList<PolicyDto>> Policies(CancellationToken ct) =>
        claims.GetMyPoliciesAsync(User.ToCurrentUser(), ct);

    /// <summary>The claimant's own claims, newest first.</summary>
    [HttpGet("claims")]
    [Authorize(Roles = nameof(UserRole.Claimant))]
    public Task<IReadOnlyList<ClaimSummaryDto>> Claims(CancellationToken ct) =>
        claims.GetMyClaimsAsync(User.ToCurrentUser(), ct);
}
