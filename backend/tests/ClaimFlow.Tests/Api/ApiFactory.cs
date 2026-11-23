using ClaimFlow.Api.Infrastructure.Persistence;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ClaimFlow.Tests.Api;

/// <summary>
/// Starts the real API in memory (including the Kafka outbox publisher and notification consumer)
/// against a SEPARATE database (claimflow_test) and a Kafka topic + consumer group unique to this test run,
/// so tests never touch dev data or dev events.
/// The test database is deleted at the start of every run; the app's normal startup migrates and seeds it.
/// Requires the docker-compose PostgreSQL and Kafka containers to be running.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestConnectionString =
        "Host=localhost;Port=5433;Database=claimflow_test;Username=claimflow;Password=claimflow_dev";
    public const string KafkaBootstrapServers = "localhost:9092";

    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];
    public string KafkaTopic => $"claim-events-test-{_runId}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // runs migrations + seeding on startup
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ClaimFlow"] = TestConnectionString,
                ["Kafka:BootstrapServers"] = KafkaBootstrapServers,
                ["Kafka:ClaimEventsTopic"] = KafkaTopic,
                ["Kafka:NotificationsConsumerGroup"] = $"claimflow-notifications-test-{_runId}"
            }));
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(TestConnectionString).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureDeletedAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync(); // stops the API and its background workers first
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = KafkaBootstrapServers }).Build();
            await admin.DeleteTopicsAsync([KafkaTopic]);
        }
        catch (KafkaException)
        {
            // best effort clean-up only
        }
    }
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
