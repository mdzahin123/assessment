using ClaimFlow.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Api.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimHistoryEntry> ClaimHistory => Set<ClaimHistoryEntry>();
    public DbSet<InformationRequest> InformationRequests => Set<InformationRequest>();

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
    }
}
