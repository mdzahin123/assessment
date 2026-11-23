using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimFlow.Api.Domain;
using ClaimFlow.Api.Features.Claims;
using ClaimFlow.Api.Features.Notifications;
using ClaimFlow.Api.Infrastructure.Messaging;
using ClaimFlow.Api.Infrastructure.Persistence;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ids = ClaimFlow.Api.Infrastructure.Persistence.DevDataSeeder.Ids;

namespace ClaimFlow.Tests.Api;

/// <summary>
/// Stage 3: transactional outbox, Kafka round trip, idempotent consumer, notifications endpoint.
/// The two "through Kafka" tests need the Kafka container running.
/// </summary>
[Collection(ApiCollection.Name)]
public class EventsAndNotificationsTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly TimeSpan KafkaTimeout = TimeSpan.FromSeconds(45);

    private HttpClient As(Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", userId.ToString());
        return client;
    }

    private async Task<ClaimDetailDto> SubmitAsAlexAsync()
    {
        var response = await As(Ids.AlexTan).PostAsJsonAsync("/api/claims", new
        {
            policyId = Ids.AlexMotorSg,
            incidentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            description = "Stage 3 test incident.",
            claimedAmount = 1_000m
        }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClaimDetailDto>(Json))!;
    }

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Async systems are "eventually" consistent: poll until the condition holds or time runs out.</summary>
    private static async Task<T> EventuallyAsync<T>(Func<Task<T?>> probe, string what) where T : class
    {
        var deadline = DateTime.UtcNow + KafkaTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await probe() is { } result)
                return result;
            await Task.Delay(250);
        }
        throw new TimeoutException($"Timed out after {KafkaTimeout.TotalSeconds}s waiting for: {what}");
    }

    private static ClaimEvent TestEvent(ClaimAction action, Guid? eventId = null) => new(
        ClaimEvent.CurrentSchemaVersion, eventId ?? Guid.NewGuid(), action, Guid.NewGuid(), "CLM-TEST01",
        Ids.AlexTan, Ids.SamLee, Market.SG, null, ClaimStatus.Submitted, Ids.AlexTan, null, DateTimeOffset.UtcNow);

    // ------------------------------------------------------------------ outbox (no Kafka needed)

    [Fact]
    public async Task Claim_change_and_its_outbox_message_are_saved_together()
    {
        var claim = await SubmitAsAlexAsync();

        var outbox = await WithDbAsync(db => db.OutboxMessages.Where(m => m.Key == claim.Id.ToString()).ToListAsync());
        var historyId = await WithDbAsync(db => db.ClaimHistory.Where(h => h.ClaimId == claim.Id).Select(h => h.Id).SingleAsync());

        var message = Assert.Single(outbox);
        Assert.Equal(historyId, message.Id);    // one event per timeline entry, sharing its ID
        Assert.Equal("Submitted", message.Type);
    }

    [Fact]
    public async Task A_rejected_action_writes_no_outbox_message()
    {
        var claim = await SubmitAsAlexAsync();

        var response = await As(Ids.SamLee).PostAsJsonAsync($"/api/claims/{claim.Id}/settle", new { amount = 10m }, Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); // not picked up yet
        Assert.Equal(1, await WithDbAsync(db => db.OutboxMessages.CountAsync(m => m.Key == claim.Id.ToString())));
    }

    // ------------------------------------------------------------------ idempotency (no Kafka needed)

    [Fact]
    public async Task Same_event_handled_twice_creates_one_notification()
    {
        var e = TestEvent(ClaimAction.Submitted);

        await WithDbAsync(async db => { await new NotificationHandler(db, NullLog()).HandleAsync(e, default); return 0; });
        await WithDbAsync(async db => { await new NotificationHandler(db, NullLog()).HandleAsync(e, default); return 0; });

        Assert.Equal(1, await WithDbAsync(db => db.Notifications.CountAsync(n => n.SourceEventId == e.EventId)));
    }

    [Fact]
    public async Task Same_event_handled_twice_at_the_same_moment_still_creates_one_notification()
    {
        var e = TestEvent(ClaimAction.Submitted);

        // Both handlers may pass the "seen it?" check before either inserts: the UNIQUE index must catch it.
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            WithDbAsync(async db => { await new NotificationHandler(db, NullLog()).HandleAsync(e, default); return 0; })));

        Assert.Equal(1, await WithDbAsync(db => db.Notifications.CountAsync(n => n.SourceEventId == e.EventId)));
    }

    // ------------------------------------------------------------------ notifications endpoint (no Kafka needed)

    [Fact]
    public async Task Users_only_see_and_mark_their_own_notifications()
    {
        var e = TestEvent(ClaimAction.Submitted);
        await WithDbAsync(async db => { await new NotificationHandler(db, NullLog()).HandleAsync(e, default); return 0; });
        var notificationId = await WithDbAsync(db => db.Notifications.Where(n => n.SourceEventId == e.EventId).Select(n => n.Id).SingleAsync());

        var alexList = await As(Ids.AlexTan).GetFromJsonAsync<List<NotificationDto>>("/api/me/notifications", Json);
        var priyaList = await As(Ids.PriyaNair).GetFromJsonAsync<List<NotificationDto>>("/api/me/notifications", Json);
        var priyaMarks = await As(Ids.PriyaNair).PostAsync($"/api/me/notifications/{notificationId}/read", null);
        var alexMarks = await As(Ids.AlexTan).PostAsync($"/api/me/notifications/{notificationId}/read", null);
        var alexListAfter = await As(Ids.AlexTan).GetFromJsonAsync<List<NotificationDto>>("/api/me/notifications", Json);

        Assert.Contains(alexList!, n => n.Id == notificationId && !n.IsRead);
        Assert.DoesNotContain(priyaList!, n => n.Id == notificationId);
        Assert.Equal(HttpStatusCode.NotFound, priyaMarks.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, alexMarks.StatusCode);
        Assert.True(alexListAfter!.Single(n => n.Id == notificationId).IsRead);
    }

    // ------------------------------------------------------------------ through Kafka (needs Kafka running)

    [Fact]
    public async Task Submitting_a_claim_reaches_the_claimant_as_a_notification_via_kafka()
    {
        var claim = await SubmitAsAlexAsync();

        // API -> outbox -> publisher -> Kafka -> consumer -> Notifications table -> API
        var notification = await EventuallyAsync(async () =>
        {
            var list = await As(Ids.AlexTan).GetFromJsonAsync<List<NotificationDto>>("/api/me/notifications", Json);
            return list!.FirstOrDefault(n => n.ClaimId == claim.Id);
        }, "notification for the new claim");

        Assert.Equal($"We have received your claim {claim.ClaimNumber}.", notification.Message);
        Assert.NotNull(await WithDbAsync(db => db.OutboxMessages.Where(m => m.Key == claim.Id.ToString()).Select(m => m.PublishedAt).SingleAsync()));
    }

    [Fact]
    public async Task All_events_for_one_claim_land_on_one_partition_in_order()
    {
        var claim = await SubmitAsAlexAsync();
        var sam = As(Ids.SamLee);
        await sam.PostAsync($"/api/claims/{claim.Id}/assign", null);
        await sam.PutAsJsonAsync($"/api/claims/{claim.Id}/reserve", new { amount = 900m }, Json);
        var withQuestion = await (await sam.PostAsJsonAsync($"/api/claims/{claim.Id}/information-requests", new { question = "Photos?" }, Json))
            .Content.ReadFromJsonAsync<ClaimDetailDto>(Json);
        await As(Ids.AlexTan).PostAsJsonAsync(
            $"/api/claims/{claim.Id}/information-requests/{withQuestion!.InformationRequests.Single().Id}/response", new { response = "Sent" }, Json);
        await sam.PostAsJsonAsync($"/api/claims/{claim.Id}/settle", new { amount = 850m }, Json);

        // Wait until the publisher has sent all 6 events for this claim...
        var key = claim.Id.ToString();
        await EventuallyAsync(async () => await WithDbAsync(db =>
            db.OutboxMessages.CountAsync(m => m.Key == key && m.PublishedAt != null)) == 6 ? "done" : null, "6 published events");

        // ...then read the topic ourselves with a separate, throwaway consumer group.
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = ApiFactory.KafkaBootstrapServers,
            GroupId = $"ordering-test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe(factory.KafkaTopic);
        var received = new List<ConsumeResult<string, string>>();
        var deadline = DateTime.UtcNow + KafkaTimeout;
        while (received.Count < 6 && DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromSeconds(1));
            if (result?.Message?.Key == key)
                received.Add(result);
        }
        consumer.Close();

        Assert.Equal(6, received.Count);
        Assert.Single(received.Select(r => r.Partition.Value).Distinct()); // same key -> same partition
        Assert.Equal(
            [ClaimAction.Submitted, ClaimAction.Assigned, ClaimAction.ReserveSet,
             ClaimAction.InformationRequested, ClaimAction.InformationProvided, ClaimAction.Settled],
            received.Select(r => JsonSerializer.Deserialize<ClaimEvent>(r.Message.Value, EventJson.Options)!.Action));
    }

    private static Microsoft.Extensions.Logging.ILogger<NotificationHandler> NullLog() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificationHandler>.Instance;
}
