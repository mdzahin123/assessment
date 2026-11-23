using System.Security.Claims;
using System.Text.Encodings.Web;
using ClaimFlow.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaimFlow.Api.Infrastructure.Auth;

/// <summary>
/// DEMO ONLY — NOT SECURE. Trusts an "X-User-Id" header and looks the user up in the database.
///
/// It plugs into ASP.NET Core's standard authentication pipeline, so replacing it with real
/// authentication (e.g. JWT from an identity provider) means swapping only this registration:
/// controllers keep using [Authorize(Roles = ...)] and User.ToCurrentUser() unchanged.
/// </summary>
public class DemoUserAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DemoUser";
    public const string HeaderName = "X-User-Id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var headerValue))
            return AuthenticateResult.NoResult(); // anonymous -> 401 on any protected endpoint

        if (!Guid.TryParse(headerValue.ToString(), out var userId))
            return AuthenticateResult.Fail($"{HeaderName} must be a GUID.");

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, Context.RequestAborted);
        if (user is null)
            return AuthenticateResult.Fail("Unknown user.");

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        ], SchemeName);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
