using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
}
