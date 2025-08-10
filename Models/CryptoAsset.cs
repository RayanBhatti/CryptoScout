namespace CryptoScout.Models;

public sealed record CryptoAsset(
    string Id,
    string Symbol,
    string Name,
    string Image,
    decimal CurrentPrice,
    int MarketCapRank,
    decimal? PriceChangePercentage1yInCurrency,
    decimal? PriceChangePercentage1y
);
