using ClaimFlow.Api.Domain;
using ClaimFlow.Api.Features.Dashboard;
using static ClaimFlow.Api.Features.Dashboard.ExposureCalculator;

namespace ClaimFlow.Tests.Dashboard;

/// <summary>Grouping and currency rules of the exposure numbers. No database.</summary>
public class ExposureCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Each_market_totals_only_its_own_claims_in_its_own_currency()
    {
        var result = Build(
        [
            new Row(Market.SG, ClaimType.Motor, 3, 10_000m, 1),
            new Row(Market.SG, ClaimType.Property, 1, 50_000m, 0),
            new Row(Market.HK, ClaimType.Property, 2, 80_000m, 1),
        ], Now);

        var sg = result.Markets.Single(m => m.Market == Market.SG);
        var hk = result.Markets.Single(m => m.Market == Market.HK);

        Assert.Equal("SGD", sg.Currency);
        Assert.Equal(60_000m, sg.ReservedAmount);   // 10,000 + 50,000, both SGD
        Assert.Equal(4, sg.OpenClaims);
        Assert.Equal(1, sg.ClaimsWithoutReserve);
        Assert.Equal("HKD", hk.Currency);
        Assert.Equal(80_000m, hk.ReservedAmount);   // untouched by SGD amounts
        Assert.Equal(2, sg.ByType.Count);
    }

    [Fact]
    public void Markets_are_listed_separately_and_in_a_stable_order()
    {
        var result = Build(
        [
            new Row(Market.AU, ClaimType.Motor, 1, 100m, 0),
            new Row(Market.SG, ClaimType.Motor, 1, 100m, 0),
        ], Now);

        Assert.Equal([Market.SG, Market.AU], result.Markets.Select(m => m.Market)); // enum order
    }

    [Fact]
    public void Every_market_has_a_currency()
    {
        foreach (var market in Enum.GetValues<Market>())
            Assert.Matches("^[A-Z]{3}$", MarketCurrency.Of(market));
    }

    [Fact]
    public void No_open_claims_means_no_markets()
    {
        Assert.Empty(Build([], Now).Markets);
    }
}
