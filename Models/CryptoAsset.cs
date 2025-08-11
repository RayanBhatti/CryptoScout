namespace CryptoScout.Models;

public sealed class CryptoAsset
{
    public string Id { get; }
    public string Symbol { get; }
    public string Name { get; }
    public string Image { get; }
    public decimal CurrentPrice { get; }
    public int MarketCapRank { get; }
    public decimal? PriceChangePercentage1y { get; }

    public CryptoAsset(
        string id,
        string symbol,
        string name,
        string image,
        decimal currentPrice,
        int marketCapRank,
        decimal? priceChangePercentage1y
    )
    {
        Id = id;
        Symbol = symbol;
        Name = name;
        Image = image;
        CurrentPrice = currentPrice;
        MarketCapRank = marketCapRank;
        PriceChangePercentage1y = priceChangePercentage1y;
    }
}
