using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimFlow.Api.Domain;
using ClaimFlow.Api.Features.Claims;
using ClaimFlow.Api.Features.Dashboard;
using Ids = ClaimFlow.Api.Infrastructure.Persistence.DevDataSeeder.Ids;

namespace ClaimFlow.Tests.Api;

/// <summary>
/// Dashboard endpoints through real HTTP + PostgreSQL. Other tests also create claims in this database,
/// so these tests compare numbers BEFORE and AFTER their own actions instead of expecting fixed totals.
/// </summary>
[Collection(ApiCollection.Name)]
public class DashboardApiTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpClient As(Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", userId.ToString());
        return client;
    }

    private async Task<TeamWorkloadDto> WorkloadAsync() =>
        (await As(Ids.MorganDavis).GetFromJsonAsync<TeamWorkloadDto>("/api/dashboard/workload", Json))!;

    private async Task<ClaimDetailDto> SubmitAsPriyaAsync(decimal amount = 800m)
    {
        var response = await As(Ids.PriyaNair).PostAsJsonAsync("/api/claims", new
        {
            policyId = Ids.PriyaMotorAu,
            incidentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            description = "Dashboard test incident.",
            claimedAmount = amount
        }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClaimDetailDto>(Json))!;
    }

    // ------------------------------------------------------------------ workload

    [Fact]
    public async Task Claimant_cannot_see_team_workload_403()
    {
        var response = await As(Ids.AlexTan).GetAsync("/api/dashboard/workload");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Officer_sees_every_officer_including_those_with_nothing_open()
    {
        var workload = await WorkloadAsync();

        Assert.Equal(
            ["Jordan Ng", "Morgan Davis", "Sam Lee"],
            workload.Officers.Select(o => o.OfficerName).Order());
        Assert.Equal(7, workload.DecisionWindowDays);
    }

    [Fact]
    public async Task Submit_pick_up_and_settle_move_the_numbers_as_expected()
    {
        var before = await WorkloadAsync();
        var morganBefore = before.Officers.Single(o => o.OfficerId == Ids.MorganDavis);

        var claim = await SubmitAsPriyaAsync();
        var afterSubmit = await WorkloadAsync();
        Assert.Equal(before.UnassignedClaims + 1, afterSubmit.UnassignedClaims);

        await As(Ids.MorganDavis).PostAsync($"/api/claims/{claim.Id}/assign", null);
        var afterPickUp = await WorkloadAsync();
        var morganAfterPickUp = afterPickUp.Officers.Single(o => o.OfficerId == Ids.MorganDavis);
        Assert.Equal(before.UnassignedClaims, afterPickUp.UnassignedClaims);
        Assert.Equal(morganBefore.OpenClaims + 1, morganAfterPickUp.OpenClaims);
        Assert.NotNull(morganAfterPickUp.AverageOpenAgeDays);

        await As(Ids.MorganDavis).PostAsJsonAsync($"/api/claims/{claim.Id}/settle", new { amount = 700m }, Json);
        var morganAfterSettle = (await WorkloadAsync()).Officers.Single(o => o.OfficerId == Ids.MorganDavis);
        Assert.Equal(morganBefore.OpenClaims, morganAfterSettle.OpenClaims);
        Assert.Equal(morganBefore.SettledLast7Days + 1, morganAfterSettle.SettledLast7Days);
        Assert.Equal(morganBefore.RejectedLast7Days, morganAfterSettle.RejectedLast7Days);
    }

    // ------------------------------------------------------------------ exposure

    private async Task<LiabilityExposureDto> ExposureAsync() =>
        (await As(Ids.MorganDavis).GetFromJsonAsync<LiabilityExposureDto>("/api/dashboard/exposure", Json))!;

    /// <summary>(open claims, reserved, without reserve) for one market+type; zeros if not present.</summary>
    private static (int Open, decimal Reserved, int WithoutReserve) Cell(LiabilityExposureDto e, Market market, ClaimType type)
    {
        var row = e.Markets.SingleOrDefault(m => m.Market == market)?.ByType.SingleOrDefault(t => t.Type == type);
        return row is null ? (0, 0m, 0) : (row.OpenClaims, row.ReservedAmount, row.ClaimsWithoutReserve);
    }

    private static (int Open, decimal Reserved, int WithoutReserve) MarketTotals(LiabilityExposureDto e, Market market)
    {
        var m = e.Markets.SingleOrDefault(x => x.Market == market);
        return m is null ? (0, 0m, 0) : (m.OpenClaims, m.ReservedAmount, m.ClaimsWithoutReserve);
    }

    [Fact]
    public async Task Claimant_cannot_see_exposure_403()
    {
        var response = await As(Ids.PriyaNair).GetAsync("/api/dashboard/exposure");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Exposure_counts_unreserved_claims_separately_adds_reserves_per_market_and_drops_closed_claims()
    {
        var before = await ExposureAsync();
        var auBefore = Cell(before, Market.AU, ClaimType.Motor);
        var sgBefore = MarketTotals(before, Market.SG);

        // 1) New claim in AU, no reserve yet: one more open claim, one more "without reserve", reserve total unchanged.
        var claim = await SubmitAsPriyaAsync(amount: 900m);
        var afterSubmit = Cell(await ExposureAsync(), Market.AU, ClaimType.Motor);
        Assert.Equal((auBefore.Open + 1, auBefore.Reserved, auBefore.WithoutReserve + 1), afterSubmit);

        // 2) Officer sets a 500 reserve: AU total +500 exactly, and SG (another currency) is untouched.
        await As(Ids.JordanNg).PostAsync($"/api/claims/{claim.Id}/assign", null);
        await As(Ids.JordanNg).PutAsJsonAsync($"/api/claims/{claim.Id}/reserve", new { amount = 500m }, Json);
        var afterReserve = await ExposureAsync();
        Assert.Equal((auBefore.Open + 1, auBefore.Reserved + 500m, auBefore.WithoutReserve), Cell(afterReserve, Market.AU, ClaimType.Motor));
        Assert.Equal(sgBefore, MarketTotals(afterReserve, Market.SG));
        Assert.Equal("AUD", afterReserve.Markets.Single(m => m.Market == Market.AU).Currency);

        // 3) Settled = closed: it no longer counts at all.
        await As(Ids.JordanNg).PostAsJsonAsync($"/api/claims/{claim.Id}/settle", new { amount = 450m }, Json);
        Assert.Equal(auBefore, Cell(await ExposureAsync(), Market.AU, ClaimType.Motor));
    }

    [Fact]
    public async Task Exposure_response_has_no_total_across_markets()
    {
        var json = await As(Ids.MorganDavis).GetStringAsync("/api/dashboard/exposure");
        var root = JsonDocument.Parse(json).RootElement;

        // Top level is only the per-market list and a timestamp: nothing that adds currencies together.
        Assert.Equal(["generatedAt", "markets"], root.EnumerateObject().Select(p => p.Name).Order());

        // Each market's total is exactly the sum of its own types.
        foreach (var market in (await ExposureAsync()).Markets)
            Assert.Equal(market.ByType.Sum(t => t.ReservedAmount), market.ReservedAmount);
    }
}
