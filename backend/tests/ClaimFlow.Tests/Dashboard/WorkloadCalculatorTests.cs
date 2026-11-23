using ClaimFlow.Api.Domain;
using ClaimFlow.Api.Features.Dashboard;
using static ClaimFlow.Api.Features.Dashboard.WorkloadCalculator;

namespace ClaimFlow.Tests.Dashboard;

/// <summary>Exact arithmetic of the team-workload numbers, with a fixed "now". No database.</summary>
public class WorkloadCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Officer Sam = new(Guid.NewGuid(), "Sam");
    private static readonly Officer Jordan = new(Guid.NewGuid(), "Jordan");

    private static OfficerWorkloadDto Row(TeamWorkloadDto result, Officer officer) =>
        result.Officers.Single(o => o.OfficerId == officer.Id);

    [Fact]
    public void Open_claims_and_average_age_are_per_officer_measured_from_submission()
    {
        var open = new List<OpenClaim>
        {
            new(Sam.Id, Now.AddDays(-2)),
            new(Sam.Id, Now.AddDays(-5)),
            new(Jordan.Id, Now.AddHours(-12)),
        };

        var result = Build([Sam, Jordan], open, [], Now);

        Assert.Equal(2, Row(result, Sam).OpenClaims);
        Assert.Equal(3.5, Row(result, Sam).AverageOpenAgeDays);       // (2 + 5) / 2
        Assert.Equal(1, Row(result, Jordan).OpenClaims);
        Assert.Equal(0.5, Row(result, Jordan).AverageOpenAgeDays);    // 12 hours
    }

    [Fact]
    public void Officer_with_nothing_open_is_still_listed_with_no_average()
    {
        var result = Build([Sam, Jordan], [new(Sam.Id, Now.AddDays(-1))], [], Now);

        var jordan = Row(result, Jordan);
        Assert.Equal(0, jordan.OpenClaims);
        Assert.Null(jordan.AverageOpenAgeDays);
    }

    [Fact]
    public void Unassigned_claims_are_counted_separately_and_not_given_to_any_officer()
    {
        var open = new List<OpenClaim> { new(null, Now.AddDays(-1)), new(null, Now.AddDays(-3)), new(Sam.Id, Now) };

        var result = Build([Sam], open, [], Now);

        Assert.Equal(2, result.UnassignedClaims);
        Assert.Equal(1, Row(result, Sam).OpenClaims);
    }

    [Fact]
    public void Only_decisions_in_the_last_7_days_by_that_officer_count()
    {
        var decisions = new List<Decision>
        {
            new(Sam.Id, ClaimAction.Settled, Now.AddDays(-1)),
            new(Sam.Id, ClaimAction.Settled, Now.AddDays(-6.9)),
            new(Sam.Id, ClaimAction.Rejected, Now.AddDays(-3)),
            new(Sam.Id, ClaimAction.Settled, Now.AddDays(-7.1)),   // too old
            new(Jordan.Id, ClaimAction.Settled, Now.AddDays(-1)),  // someone else
        };

        var sam = Row(Build([Sam, Jordan], [], decisions, Now), Sam);

        Assert.Equal(2, sam.SettledLast7Days);
        Assert.Equal(1, sam.RejectedLast7Days);
    }

    [Fact]
    public void Busiest_officer_is_listed_first()
    {
        var result = Build([Sam, Jordan], [new(Jordan.Id, Now), new(Jordan.Id, Now)], [], Now);

        Assert.Equal(["Jordan", "Sam"], result.Officers.Select(o => o.OfficerName));
    }
}
