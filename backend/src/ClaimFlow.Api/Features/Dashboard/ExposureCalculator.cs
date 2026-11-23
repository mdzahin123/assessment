using ClaimFlow.Api.Domain;

namespace ClaimFlow.Api.Features.Dashboard;

/// <summary>
/// Groups per-(market, type) totals into per-market exposure. Totals are only ever added up WITHIN a
/// market (same currency), never across markets. Pure function, unit-tested.
/// </summary>
public static class ExposureCalculator
{
    /// <summary>One row of the SQL GROUP BY (market, type) over open claims.</summary>
    public sealed record Row(Market Market, ClaimType Type, int OpenClaims, decimal ReservedAmount, int ClaimsWithoutReserve);

    public static LiabilityExposureDto Build(IEnumerable<Row> rows, DateTimeOffset now)
    {
        var markets = rows
            .GroupBy(r => r.Market)
            .OrderBy(g => g.Key)
            .Select(market =>
            {
                var byType = market
                    .OrderBy(r => r.Type)
                    .Select(r => new ExposureByTypeDto(r.Type, r.OpenClaims, r.ReservedAmount, r.ClaimsWithoutReserve))
                    .ToList();

                return new MarketExposureDto(
                    market.Key,
                    MarketCurrency.Of(market.Key),
                    byType.Sum(t => t.OpenClaims),
                    byType.Sum(t => t.ReservedAmount),   // same market = same currency: safe to add
                    byType.Sum(t => t.ClaimsWithoutReserve),
                    byType);
            })
            .ToList();

        return new LiabilityExposureDto(markets, now);
    }
}
