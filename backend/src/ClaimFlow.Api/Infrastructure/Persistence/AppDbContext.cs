using ClaimFlow.Api.Domain;
using ClaimFlow.Api.Features.Notifications;
using ClaimFlow.Api.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Api.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>PostgreSQL sequence used to generate human-readable claim numbers (CLM-001000, ...).</summary>
    public const string ClaimNumberSequence = "ClaimNumbers";

    public DbSet<User> Users => Set<User>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimHistoryEntry> ClaimHistory => Set<ClaimHistoryEntry>();
    public DbSet<InformationRequest> InformationRequests => Set<InformationRequest>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<Notification> Notifications => Set<Notification>();

    // ------------------------------------------------------------------ transactional outbox

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AddOutboxMessagesForNewHistory();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AddOutboxMessagesForNewHistory();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Every new claim timeline entry becomes an outbox message, inside the SAME SaveChanges call,
    /// so both are written in one database transaction: either both exist or neither does.
    /// Because it hooks into saving itself, no endpoint can "forget" to publish an event.
    /// </summary>
    private void AddOutboxMessagesForNewHistory()
    {
        // Entries() runs EF's change detection first, so history entries just added to a claim's list are found.
        var newEntries = ChangeTracker.Entries<ClaimHistoryEntry>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();
        if (newEntries.Count == 0)
            return;

        var trackedClaims = ChangeTracker.Entries<Claim>().ToDictionary(e => e.Entity.Id, e => e.Entity);
        var alreadyQueued = ChangeTracker.Entries<OutboxMessage>().Select(e => e.Entity.Id).ToHashSet();

        foreach (var entry in newEntries.Where(h => !alreadyQueued.Contains(h.Id))) // safe if SaveChanges is retried
            OutboxMessages.Add(OutboxMessage.For(ClaimEvent.From(trackedClaims[entry.ClaimId], entry)));
    }

    /// <summary>Rules applied to every property of a given C# type, across all tables.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money: exact base-10 numbers, 2 decimal places -> PostgreSQL numeric(18,2).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        // Store enums as readable text ("InReview") instead of numbers (1), so the data makes sense
        // in psql and reordering the C# enum can never silently change meaning of existing rows.
        configurationBuilder.Properties<UserRole>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ClaimType>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<Market>().HaveConversion<string>().HaveMaxLength(2);
        configurationBuilder.Properties<ClaimStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ClaimAction>().HaveConversion<string>().HaveMaxLength(30);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Starts at 1000 so live claim numbers never collide with the seeded CLM-000001..CLM-000006.
        modelBuilder.HasSequence<long>(ClaimNumberSequence).StartsAt(1000);

        // Our code creates all IDs itself (Guid.CreateVersion7()). Telling EF "never generate these"
        // matters: otherwise a NEW history entry added to an EXISTING claim, already carrying an Id,
        // would be mistaken for an existing row and EF would try to UPDATE it instead of INSERT.
        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.Id).ValueGeneratedNever();
            e.Property(u => u.Name).HasMaxLength(200);
            e.Property(u => u.Email).HasMaxLength(320);
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<Policy>(e =>
        {
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.PolicyNumber).HasMaxLength(30);
            e.HasIndex(p => p.PolicyNumber).IsUnique();
            e.HasOne(p => p.Holder).WithMany().HasForeignKey(p => p.HolderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Claim>(e =>
        {
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.ClaimNumber).HasMaxLength(30);
            e.HasIndex(c => c.ClaimNumber).IsUnique();
            e.Property(c => c.Description).HasMaxLength(Claim.DescriptionMaxLength);
            e.Property(c => c.RejectionReason).HasMaxLength(Claim.TextMaxLength);

            // Optimistic concurrency via PostgreSQL's built-in xmin system column (no extra column needed).
            e.Property(c => c.Version).IsRowVersion();

            // Restrict = refuse to delete a user/policy that still has claims (insurance data is never cascaded away).
            e.HasOne(c => c.Policy).WithMany().HasForeignKey(c => c.PolicyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.Claimant).WithMany().HasForeignKey(c => c.ClaimantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.AssignedOfficer).WithMany().HasForeignKey(c => c.AssignedOfficerId).OnDelete(DeleteBehavior.Restrict);

            // History and info requests belong to the claim; EF reads/writes them through the private lists.
            e.HasMany(c => c.History).WithOne().HasForeignKey(h => h.ClaimId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(c => c.InformationRequests).WithOne().HasForeignKey(r => r.ClaimId).OnDelete(DeleteBehavior.Cascade);

            // Indexes for the officer work queue and dashboards (filter by status / assignee).
            e.HasIndex(c => c.Status);
            e.HasIndex(c => c.AssignedOfficerId);
            e.HasIndex(c => c.ClaimantId);
        });

        modelBuilder.Entity<ClaimHistoryEntry>(e =>
        {
            e.ToTable("ClaimHistory");
            e.Property(h => h.Id).ValueGeneratedNever();
            e.Property(h => h.Note).HasMaxLength(Claim.TextMaxLength);
            e.HasIndex(h => new { h.ClaimId, h.OccurredAt });
        });

        modelBuilder.Entity<InformationRequest>(e =>
        {
            e.Property(r => r.Id).ValueGeneratedNever();
            e.Property(r => r.Question).HasMaxLength(Claim.TextMaxLength);
            e.Property(r => r.Response).HasMaxLength(Claim.TextMaxLength);
        });

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.Property(o => o.Id).ValueGeneratedNever();
            e.Property(o => o.Type).HasMaxLength(50);
            e.Property(o => o.Key).HasMaxLength(100);
            e.Property(o => o.Payload).HasColumnType("jsonb");
            e.Property(o => o.LastError).HasMaxLength(1000);
            // Partial index over unpublished rows only: exactly what the publisher looks for every second.
            e.HasIndex(o => new { o.OccurredAt, o.Id }).HasFilter("\"PublishedAt\" IS NULL");
        });

        modelBuilder.Entity<Notification>(e =>
        {
            e.Property(n => n.Id).ValueGeneratedNever();
            e.Property(n => n.ClaimNumber).HasMaxLength(30);
            e.Property(n => n.Message).HasMaxLength(1200);
            // Idempotency guarantee: one notification per event, even if Kafka delivers the event twice.
            e.HasIndex(n => n.SourceEventId).IsUnique();
            e.HasIndex(n => new { n.UserId, n.CreatedAt });
            e.HasOne<User>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
