using ClaimFlow.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Api.Infrastructure.Persistence;

/// <summary>
/// Fictional demo data for local development. Runs once, on an empty database.
/// Claims are built with the real domain methods (Submit, AssignTo, Settle...), so seeded data
/// obeys the same rules as live data and every claim has a complete history.
/// IDs are fixed so they can be used directly when testing the API (e.g. as X-User-Id).
/// </summary>
public static class DevDataSeeder
{
    public static class Ids
    {
        public static readonly Guid AlexTan = Guid.Parse("11111111-0000-0000-0000-000000000001");
        public static readonly Guid PriyaNair = Guid.Parse("11111111-0000-0000-0000-000000000002");

        public static readonly Guid SamLee = Guid.Parse("22222222-0000-0000-0000-000000000001");
        public static readonly Guid JordanNg = Guid.Parse("22222222-0000-0000-0000-000000000002");
        public static readonly Guid MorganDavis = Guid.Parse("22222222-0000-0000-0000-000000000003");

        public static readonly Guid AlexMotorSg = Guid.Parse("33333333-0000-0000-0000-000000000001");
        public static readonly Guid AlexPropertySg = Guid.Parse("33333333-0000-0000-0000-000000000002");
        public static readonly Guid PriyaMotorAu = Guid.Parse("33333333-0000-0000-0000-000000000003");
        public static readonly Guid PriyaPropertyHk = Guid.Parse("33333333-0000-0000-0000-000000000004");
    }

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct))
            return; // already seeded

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        // Each step uses a distinct time (h = hours later that day). Identical timestamps would leave the
        // timeline order undefined: a first run showed "ReserveSet" listed before "Assigned".
        DateTimeOffset DaysAgo(int d, int h = 0) => now.AddDays(-d).AddHours(h);

        // People (fictional)
        db.Users.AddRange(
            new User(Ids.AlexTan, "Alex Tan", "alex.tan@example.com", UserRole.Claimant),
            new User(Ids.PriyaNair, "Priya Nair", "priya.nair@example.com", UserRole.Claimant),
            new User(Ids.SamLee, "Sam Lee", "sam.lee@example.com", UserRole.Officer),
            new User(Ids.JordanNg, "Jordan Ng", "jordan.ng@example.com", UserRole.Officer),
            new User(Ids.MorganDavis, "Morgan Davis", "morgan.davis@example.com", UserRole.Officer));

        // Policies, valid around "today" so the demo keeps working whenever it is run.
        var validFrom = today.AddDays(-300);
        var validTo = today.AddDays(65);
        var alexMotor = new Policy(Ids.AlexMotorSg, "MOT-SG-0001", Ids.AlexTan, ClaimType.Motor, Market.SG, 50_000m, validFrom, validTo);
        var alexProperty = new Policy(Ids.AlexPropertySg, "PRP-SG-0002", Ids.AlexTan, ClaimType.Property, Market.SG, 500_000m, validFrom, validTo);
        var priyaMotor = new Policy(Ids.PriyaMotorAu, "MOT-AU-0003", Ids.PriyaNair, ClaimType.Motor, Market.AU, 80_000m, validFrom, validTo);
        var priyaProperty = new Policy(Ids.PriyaPropertyHk, "PRP-HK-0004", Ids.PriyaNair, ClaimType.Property, Market.HK, 1_000_000m, validFrom, validTo);
        db.Policies.AddRange(alexMotor, alexProperty, priyaMotor, priyaProperty);

        // Claims in every status, built through the domain methods.
        var c1 = Claim.Submit(alexMotor, Ids.AlexTan, "CLM-000001", today.AddDays(-2),
            "Rear-ended while stopped at traffic lights. Rear bumper and boot lid damaged.", 4_200m, DaysAgo(1));

        var c2 = Claim.Submit(priyaMotor, Ids.PriyaNair, "CLM-000002", today.AddDays(-4),
            "Side mirror broken by a passing vehicle in a car park.", 1_650m, DaysAgo(3));

        var c3 = Claim.Submit(priyaProperty, Ids.PriyaNair, "CLM-000003", today.AddDays(-8),
            "Burst pipe in the kitchen wall flooded the kitchen and hallway.", 25_000m, DaysAgo(6));
        c3.AssignTo(Ids.SamLee, DaysAgo(5));
        c3.SetReserve(Ids.SamLee, 18_000m, DaysAgo(5, h: 2));

        var c4 = Claim.Submit(alexProperty, Ids.AlexTan, "CLM-000004", today.AddDays(-12),
            "Water leak from the unit above damaged the bedroom ceiling.", 9_800m, DaysAgo(10));
        c4.AssignTo(Ids.JordanNg, DaysAgo(9));
        c4.SetReserve(Ids.JordanNg, 7_500m, DaysAgo(9, h: 2));
        c4.RequestInformation(Ids.JordanNg, "Please provide the repair quotation and photos of the ceiling damage.", DaysAgo(8));

        var c5 = Claim.Submit(priyaMotor, Ids.PriyaNair, "CLM-000005", today.AddDays(-22),
            "Windscreen cracked by a stone on the motorway.", 3_100m, DaysAgo(20));
        c5.AssignTo(Ids.SamLee, DaysAgo(19));
        c5.SetReserve(Ids.SamLee, 3_000m, DaysAgo(19, h: 2));
        c5.Settle(Ids.SamLee, 2_850m, DaysAgo(15));

        var c6 = Claim.Submit(alexMotor, Ids.AlexTan, "CLM-000006", today.AddDays(-31),
            "Collision with a bollard while making a delivery.", 12_000m, DaysAgo(30));
        c6.AssignTo(Ids.JordanNg, DaysAgo(29));
        c6.Reject(Ids.JordanNg, "Vehicle was being used for commercial delivery, which is excluded under the policy terms.", DaysAgo(25));

        db.Claims.AddRange(c1, c2, c3, c4, c5, c6);

        await db.SaveChangesAsync(ct);
    }
}
