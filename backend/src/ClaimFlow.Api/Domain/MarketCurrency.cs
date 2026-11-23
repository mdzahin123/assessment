namespace ClaimFlow.Api.Domain;

/// <summary>
/// Each market has its own currency. Amounts from different markets must never be added together:
/// 1,000 SGD + 1,000 HKD is not 2,000 of anything.
/// </summary>
public static class MarketCurrency
{
    public static string Of(Market market) => market switch
    {
        Market.SG => "SGD",
        Market.HK => "HKD",
        Market.AU => "AUD",
        Market.MY => "MYR",
        Market.TH => "THB",
        Market.ID => "IDR",
        _ => throw new ArgumentOutOfRangeException(nameof(market), market, "No currency defined for this market.")
    };
}
