using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimFlow.Api.Domain;
using ClaimFlow.Api.Features.Claims;
using ClaimFlow.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ids = ClaimFlow.Api.Infrastructure.Persistence.DevDataSeeder.Ids;

namespace ClaimFlow.Tests.Api;

/// <summary>
/// End-to-end HTTP tests: real routing, auth handler, validation, error mapping, EF Core and PostgreSQL.
/// Each test creates its own fresh claim(s) so tests don't depend on each other's leftovers.
/// </summary>
[Collection(ApiCollection.Name)]
public class ClaimsApiTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static DateOnly Yesterday => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

    private HttpClient As(Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", userId.ToString());
        return client;
    }

    private async Task<ClaimDetailDto> SubmitAsAlexAsync(decimal amount = 1_000m)
    {
        var response = await As(Ids.AlexTan).PostAsJsonAsync("/api/claims", new
        {
            policyId = Ids.AlexMotorSg,
            incidentDate = Yesterday,
            description = "Test incident: scraped door in car park.",
            claimedAmount = amount
        }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClaimDetailDto>(Json))!;
    }

    private static async Task<T> Read<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    // ------------------------------------------------------------------ authentication

    [Fact]
    public async Task Request_without_X_User_Id_is_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/me/claims");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_with_unknown_user_is_401()
    {
        var response = await As(Guid.NewGuid()).GetAsync("/api/me/claims");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task User_list_is_public_for_the_login_picker()
    {
        var users = await factory.CreateClient().GetFromJsonAsync<List<UserDto>>("/api/users", Json);
        Assert.Equal(5, users!.Count);
    }

    // ------------------------------------------------------------------ submit + validation

    [Fact]
    public async Task Claimant_submits_claim_and_gets_201_with_location_and_claim_number()
    {
        var response = await As(Ids.AlexTan).PostAsJsonAsync("/api/claims", new
        {
            policyId = Ids.AlexMotorSg, incidentDate = Yesterday, description = "Hail damage to roof.", claimedAmount = 2_500.75m
        }, Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var claim = await Read<ClaimDetailDto>(response);
        Assert.Equal($"/api/claims/{claim.Id}", response.Headers.Location!.AbsolutePath, ignoreCase: true);
        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.Matches(@"^CLM-\d{6}$", claim.ClaimNumber);
        Assert.True(int.Parse(claim.ClaimNumber[4..]) >= 1000); // from the sequence, never clashes with seed data
        Assert.Equal("Alex Tan", Assert.Single(claim.History).ActorName);
    }

    [Fact]
    public async Task Submit_with_missing_fields_is_400_listing_each_field()
    {
        var response = await As(Ids.AlexTan).PostAsJsonAsync("/api/claims", new { description = "No other fields" }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await Read<ValidationProblemDetails>(response);
        Assert.Contains("PolicyId", problem.Errors.Keys);
        Assert.Contains("IncidentDate", problem.Errors.Keys);
        Assert.Contains("ClaimedAmount", problem.Errors.Keys);
    }

    [Fact]
    public async Task Submit_breaking_a_business_rule_is_400_with_the_rule_message()
    {
        var response = await As(Ids.AlexTan).PostAsJsonAsync("/api/claims", new
        {
            policyId = Ids.AlexMotorSg, incidentDate = Yesterday, description = "Negative", claimedAmount = -5m
        }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Claimed amount must be greater than zero.", (await Read<ProblemDetails>(response)).Detail);
    }

    [Fact]
    public async Task Submit_against_another_claimants_policy_is_400_policy_not_found()
    {
        var response = await As(Ids.AlexTan).PostAsJsonAsync("/api/claims", new
        {
            policyId = Ids.PriyaMotorAu, incidentDate = Yesterday, description = "Not my policy", claimedAmount = 100m
        }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Policy not found.", (await Read<ProblemDetails>(response)).Detail);
    }

    [Fact]
    public async Task Officer_cannot_submit_a_claim_403()
    {
        var response = await As(Ids.SamLee).PostAsJsonAsync("/api/claims", new
        {
            policyId = Ids.AlexMotorSg, incidentDate = Yesterday, description = "x", claimedAmount = 100m
        }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ------------------------------------------------------------------ ownership

    [Fact]
    public async Task Claimant_cannot_view_another_claimants_claim_404()
    {
        var alexClaim = await SubmitAsAlexAsync();

        var asPriya = await As(Ids.PriyaNair).GetAsync($"/api/claims/{alexClaim.Id}");
        var asAlex = await As(Ids.AlexTan).GetAsync($"/api/claims/{alexClaim.Id}");

        Assert.Equal(HttpStatusCode.NotFound, asPriya.StatusCode); // same answer as a non-existent claim
        Assert.Equal(HttpStatusCode.OK, asAlex.StatusCode);
    }

    [Fact]
    public async Task Claimant_cannot_answer_a_question_on_another_claimants_claim_404()
    {
        var claim = await SubmitAsAlexAsync();
        await As(Ids.SamLee).PostAsync($"/api/claims/{claim.Id}/assign", null);
        var withQuestion = await Read<ClaimDetailDto>(await As(Ids.SamLee).PostAsJsonAsync(
            $"/api/claims/{claim.Id}/information-requests", new { question = "Photos?" }, Json));
        var requestId = withQuestion.InformationRequests.Single().Id;

        var response = await As(Ids.PriyaNair).PostAsJsonAsync(
            $"/api/claims/{claim.Id}/information-requests/{requestId}/response", new { response = "Hijack" }, Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task My_claims_only_lists_the_callers_own_claims()
    {
        await SubmitAsAlexAsync();

        var priyaClaims = await As(Ids.PriyaNair).GetFromJsonAsync<List<ClaimSummaryDto>>("/api/me/claims", Json);

        Assert.NotEmpty(priyaClaims!);
        Assert.All(priyaClaims!, c => Assert.Equal("Priya Nair", c.ClaimantName));
    }

    [Fact]
    public async Task Claimant_cannot_use_officer_endpoints_403()
    {
        var claim = await SubmitAsAlexAsync();

        var assign = await As(Ids.AlexTan).PostAsync($"/api/claims/{claim.Id}/assign", null);
        var queue = await As(Ids.AlexTan).GetAsync("/api/claims");

        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, queue.StatusCode);
    }

    [Fact]
    public async Task Unknown_claim_is_404()
    {
        var response = await As(Ids.SamLee).GetAsync($"/api/claims/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------------ workflow

    [Fact]
    public async Task Full_journey_through_the_api_saves_every_step()
    {
        var claim = await SubmitAsAlexAsync(amount: 3_000m);
        var sam = As(Ids.SamLee);
        var alex = As(Ids.AlexTan);

        Assert.Equal(HttpStatusCode.OK, (await sam.PostAsync($"/api/claims/{claim.Id}/assign", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sam.PutAsJsonAsync($"/api/claims/{claim.Id}/reserve", new { amount = 2_800m }, Json)).StatusCode);
        var awaiting = await Read<ClaimDetailDto>(await sam.PostAsJsonAsync(
            $"/api/claims/{claim.Id}/information-requests", new { question = "Police report number?" }, Json));
        Assert.Equal(ClaimStatus.AwaitingInfo, awaiting.Status);

        var answered = await Read<ClaimDetailDto>(await alex.PostAsJsonAsync(
            $"/api/claims/{claim.Id}/information-requests/{awaiting.InformationRequests.Single().Id}/response",
            new { response = "PR-12345" }, Json));
        Assert.Equal(ClaimStatus.InReview, answered.Status);

        var settled = await Read<ClaimDetailDto>(await sam.PostAsJsonAsync(
            $"/api/claims/{claim.Id}/settle", new { amount = 2_750m }, Json));
        Assert.Equal(ClaimStatus.Settled, settled.Status);

        // Re-read from the database: proves new history/info-request rows on an EXISTING claim were INSERTed.
        var reloaded = await sam.GetFromJsonAsync<ClaimDetailDto>($"/api/claims/{claim.Id}", Json);
        Assert.Equal(
            [ClaimAction.Submitted, ClaimAction.Assigned, ClaimAction.ReserveSet,
             ClaimAction.InformationRequested, ClaimAction.InformationProvided, ClaimAction.Settled],
            reloaded!.History.Select(h => h.Action));
        Assert.Equal(2_750m, reloaded.SettledAmount);
        Assert.Equal("Sam Lee", reloaded.AssignedOfficerName);
        Assert.Equal("PR-12345", reloaded.InformationRequests.Single().Response);
    }

    [Fact]
    public async Task Claimant_view_hides_the_internal_reserve_but_officer_view_shows_it()
    {
        var claim = await SubmitAsAlexAsync(amount: 5_000m);
        await As(Ids.SamLee).PostAsync($"/api/claims/{claim.Id}/assign", null);
        await As(Ids.SamLee).PutAsJsonAsync($"/api/claims/{claim.Id}/reserve", new { amount = 4_200m }, Json);

        var officerView = await As(Ids.SamLee).GetFromJsonAsync<ClaimDetailDto>($"/api/claims/{claim.Id}", Json);
        var claimantView = await As(Ids.AlexTan).GetFromJsonAsync<ClaimDetailDto>($"/api/claims/{claim.Id}", Json);
        var claimantList = await As(Ids.AlexTan).GetFromJsonAsync<List<ClaimSummaryDto>>("/api/me/claims", Json);

        Assert.Equal(4_200m, officerView!.ReserveAmount);
        Assert.Contains(officerView.History, h => h.Action == ClaimAction.ReserveSet);
        Assert.Null(claimantView!.ReserveAmount);
        Assert.DoesNotContain(claimantView.History, h => h.Action == ClaimAction.ReserveSet);
        Assert.Null(claimantList!.Single(c => c.Id == claim.Id).ReserveAmount);
    }

    [Fact]
    public async Task Settling_a_rejected_claim_is_409()
    {
        var claim = await SubmitAsAlexAsync();
        var sam = As(Ids.SamLee);
        await sam.PostAsync($"/api/claims/{claim.Id}/assign", null);
        await sam.PostAsJsonAsync($"/api/claims/{claim.Id}/reject", new { reason = "Excluded" }, Json);

        var response = await sam.PostAsJsonAsync($"/api/claims/{claim.Id}/settle", new { amount = 100m }, Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Cannot settle a claim that is Rejected.", (await Read<ProblemDetails>(response)).Detail);
    }

    [Fact]
    public async Task Officer_cannot_settle_a_claim_assigned_to_a_colleague_403()
    {
        var claim = await SubmitAsAlexAsync();
        await As(Ids.SamLee).PostAsync($"/api/claims/{claim.Id}/assign", null);

        var response = await As(Ids.JordanNg).PostAsJsonAsync($"/api/claims/{claim.Id}/settle", new { amount = 100m }, Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Queue_defaults_to_open_claims_and_filters_unassigned()
    {
        await SubmitAsAlexAsync();
        var sam = As(Ids.SamLee);

        var open = await sam.GetFromJsonAsync<List<ClaimSummaryDto>>("/api/claims", Json);
        var unassigned = await sam.GetFromJsonAsync<List<ClaimSummaryDto>>("/api/claims?assignee=unassigned", Json);

        Assert.DoesNotContain(open!, c => c.Status is ClaimStatus.Settled or ClaimStatus.Rejected);
        Assert.NotEmpty(unassigned!);
        Assert.All(unassigned!, c => Assert.Null(c.AssignedOfficerId));
        Assert.Equal(open!.OrderBy(c => c.CreatedAt).Select(c => c.Id), open!.Select(c => c.Id)); // oldest first
    }

    // ------------------------------------------------------------------ concurrency

    [Fact]
    public async Task Two_officers_picking_up_the_same_claim_at_once_only_one_wins()
    {
        var claim = await SubmitAsAlexAsync();

        var responses = await Task.WhenAll(
            As(Ids.SamLee).PostAsync($"/api/claims/{claim.Id}/assign", null),
            As(Ids.JordanNg).PostAsync($"/api/claims/{claim.Id}/assign", null));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        var final = await As(Ids.MorganDavis).GetFromJsonAsync<ClaimDetailDto>($"/api/claims/{claim.Id}", Json);
        Assert.Single(final!.History, h => h.Action == ClaimAction.Assigned);
    }

    /// <summary>
    /// Deterministic version of the race above: both officers have LOADED the claim before either saves,
    /// so the domain rule can't catch it — only the xmin version check can.
    /// </summary>
    [Fact]
    public async Task Stale_save_is_rejected_by_the_xmin_version_check()
    {
        var claim = await SubmitAsAlexAsync();
        using var scopeA = factory.Services.CreateScope();
        using var scopeB = factory.Services.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();

        var samsCopy = await dbA.Claims.SingleAsync(c => c.Id == claim.Id);
        var jordansCopy = await dbB.Claims.SingleAsync(c => c.Id == claim.Id); // same version as Sam's

        samsCopy.AssignTo(Ids.SamLee, DateTimeOffset.UtcNow);
        await dbA.SaveChangesAsync();                                  // Sam wins; row version changes

        jordansCopy.AssignTo(Ids.JordanNg, DateTimeOffset.UtcNow);     // allowed in memory: Jordan's copy is stale
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());
    }
}
