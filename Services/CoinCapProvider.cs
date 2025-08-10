using System.Net.Http.Json;
using System.Text.Json;
using CryptoScout.Models;
using Microsoft.Extensions.Caching.Memory;

namespace CryptoScout.Services;

public sealed class CoinCapProvider(HttpClient http, IMemoryCache cache, ILogger<CoinCapProvider> log) : ICryptoDataProvider
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<CryptoAsset>> GetTop100Async(string vsCurrency = "usd", CancellationToken ct = default)
    {
        // USD-native; keep arg for interface compatibility
        const string cacheKey = "coincap:top100";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<CryptoAsset>? cached) && cached is not null)
            return cached;

        // 1) Top-100 by market cap
        var assets = await http.GetFromJsonAsync<CoinCapAssets>(
            "https://api.coincap.io/v2/assets?limit=100", J, ct) ?? new();

        // 2) Compute 1y % from daily history (≈ last 365d)
        var end = DateTimeOffset.UtcNow;
        var start = end.AddDays(-365);

        var throttler = new SemaphoreSlim(6);
        var list = new List<CryptoAsset>(assets.Data.Count);

        await Parallel.ForEachAsync(assets.Data, ct, async (a, token) =>
        {
            await throttler.WaitAsync(token);
            try
            {
                decimal? pct1y = null;
                try
                {
                    var url = $"https://api.coincap.io/v2/assets/{a.Id}/history?interval=d1&start={start.ToUnixTimeMilliseconds()}&end={end.ToUnixTimeMilliseconds()}";
                    var hist = await http.GetFromJsonAsync<CoinCapHistory>(url, J, token);
                    var points = hist?.Data;
                    if (points is { Count: > 2 })
                    {
                        var first = decimal.Parse(points.First().PriceUsd);
                        var last  = decimal.Parse(points.Last().PriceUsd);
                        if (first > 0) pct1y = (last - first) / first * 100m;
                    }
                }
                catch (Exception ex)
                {
                    log.LogDebug(ex, "history fail for {id}", a.Id);
                }

                lock (list)
                {
                    list.Add(new CryptoAsset(
                        a.Id,
                        a.Symbol,
                        a.Name,
                        Image: $"https://assets.coincap.io/assets/icons/{a.Symbol.ToLower()}@2x.png",
                        CurrentPrice: decimal.TryParse(a.PriceUsd, out var p) ? p : 0,
                        MarketCapRank: int.TryParse(a.Rank, out var r) ? r : 10_000,
                        PriceChangePercentage1yInCurrency: null,
                        PriceChangePercentage1y: pct1y
                    ));
                }
            }
            finally { throttler.Release(); }
        });

        var ordered = list.OrderBy(x => x.MarketCapRank).ToList();
        cache.Set(cacheKey, ordered, TimeSpan.FromMinutes(5));
        return ordered;
    }

    // === DTOs (minimal) ===
    private sealed class CoinCapAssets
    {
        public List<Asset> Data { get; set; } = [];
        public record Asset(string Id, string Rank, string Symbol, string Name, string PriceUsd);
    }

    private sealed class CoinCapHistory
    {
        public List<Point> Data { get; set; } = [];
        public record Point(string PriceUsd, long Time);
    }
}
