using ClaimFlow.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimFlow.Api.Features.Dashboard;

/// <summary>
/// Team workload and liability exposure. Claims staff (officers) only: claimants get 403.
/// (No separate Manager role: the brief defines two user types; see docs/ARCHITECTURE.md.)
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = nameof(UserRole.Officer))]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class DashboardController(DashboardService dashboard) : ControllerBase
{
    /// <summary>Per officer: open claims, their average age, decisions in the last 7 days; plus unassigned count.</summary>
    [HttpGet("workload")]
    [ProducesResponseType<TeamWorkloadDto>(StatusCodes.Status200OK)]
    public Task<TeamWorkloadDto> Workload(CancellationToken ct) => dashboard.GetWorkloadAsync(ct);

    /// <summary>Reserves on open claims per market (in that market's currency) and type. Markets are never summed together.</summary>
    [HttpGet("exposure")]
    [ProducesResponseType<LiabilityExposureDto>(StatusCodes.Status200OK)]
    public Task<LiabilityExposureDto> Exposure(CancellationToken ct) => dashboard.GetExposureAsync(ct);
}
