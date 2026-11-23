using ClaimFlow.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ClaimFlow.Tests.Api;

/// <summary>
/// Starts the real API in memory against a SEPARATE database (claimflow_test) so tests never touch dev data.
/// The test database is deleted at the start of every run; the app's normal startup then migrates and seeds it.
/// Requires the docker-compose PostgreSQL container to be running.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestConnectionString =
        "Host=localhost;Port=5433;Database=claimflow_test;Username=claimflow;Password=claimflow_dev";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // runs migrations + seeding on startup
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ClaimFlow"] = TestConnectionString
            }));
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(TestConnectionString).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureDeletedAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => base.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
