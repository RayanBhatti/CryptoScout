using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CryptoScout.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CryptoScout.Services;

public sealed class CoinGeckoProvider(HttpClient http, IMemoryCache cache, ILogger<CoinGeckoProvider> log) : ICryptoDataProvider
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private const string Base = "https://api.coingecko.com/api/v3";

    public async Task<IReadOnlyList<CryptoAsset>> GetTop100Async(string vsCurrency = "usd", CancellationToken ct = default)
    {
        const string cacheKey = "coingecko:top100";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<CryptoAsset>? cached) && cached is not null)
            return cached;

        // Free Demo key (no charge). Put in .env.local as COINGECKO_API_KEY=...
        var demoKey = Environment.GetEnvironmentVariable("COINGECKO_API_KEY");

        var url = $"{Base}/coins/markets" +
                  $"?vs_currency={Uri.EscapeDataString(vsCurrency)}" +
                  $"&order=market_cap_desc&per_page=100&page=1" +
                  $"&price_change_percentage=1y&locale=en";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(demoKey))
            req.Headers.Add("x-cg-demo-api-key", demoKey); // Demo API header

        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            string body = "";
            try { body = await resp.Content.ReadAsStringAsync(ct); } catch { }
            if (body.Length > 200) body = body[..200];
            log.LogWarning("CoinGecko GET {Url} -> {Status}. Body: {Body}", url, (int)resp.StatusCode, body);
            throw new HttpRequestException("CoinGecko markets endpoint failed.");
        }

        var items = await resp.Content.ReadFromJsonAsync<List<CGMarket>>(J, ct) ?? [];
        var list = new List<CryptoAsset>(items.Count);

        foreach (var m in items)
        {
            var price = m.current_price ?? 0m;
            var rank = m.market_cap_rank ?? int.MaxValue;
            decimal? pct1y = m.price_change_percentage_1y_in_currency is double d ? (decimal)d : null;

            list.Add(new CryptoAsset(
                Id: m.id ?? "",
                Symbol: (m.symbol ?? "").ToLowerInvariant(),
                Name: m.name ?? "",
                Image: m.image ?? "",
                CurrentPrice: price,
                MarketCapRank: rank,
                PriceChangePercentage1yInCurrency: pct1y,
                PriceChangePercentage1y: pct1y
            ));
        }

        var ordered = list.OrderBy(x => x.MarketCapRank).ToList();
        // 30-minute cache => “tick” cadence
        cache.Set(cacheKey, ordered, TimeSpan.FromMinutes(30));
        return ordered;
    }

    // --- minimal DTO ---
    private sealed class CGMarket
    {
        public string? id { get; set; }
        public string? symbol { get; set; }
        public string? name { get; set; }
        public string? image { get; set; }
        public decimal? current_price { get; set; }
        public int? market_cap_rank { get; set; }
        public double? price_change_percentage_1y_in_currency { get; set; }
    }
}
