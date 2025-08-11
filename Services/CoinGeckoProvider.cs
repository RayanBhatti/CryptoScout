using System.Text.Json;
using CryptoScout.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CryptoScout.Services;

public sealed class CoinGeckoProvider : ICryptoDataProvider
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CoinGeckoProvider> _log;

    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    public CoinGeckoProvider(HttpClient http, IMemoryCache cache, ILogger<CoinGeckoProvider> log)
    {
        _http = http;
        _cache = cache;
        _log = log;
    }

    public async Task<IReadOnlyList<CryptoAsset>> GetTop100Async(string vsCurrency = "usd", CancellationToken ct = default)
    {
        var cacheKey = $"cg.top100.{vsCurrency}";
        if (_cache.TryGetValue(cacheKey, out List<CryptoAsset>? cached) && cached is not null)
            return cached;

        var url =
            $"https://api.coingecko.com/api/v3/coins/markets" +
            $"?vs_currency={Uri.EscapeDataString(vsCurrency)}" +
            $"&order=market_cap_desc&per_page=100&sparkline=false&price_change_percentage=1y";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);

        // Optional demo key header
        var demoKey = Environment.GetEnvironmentVariable("COINGECKO_API_KEY");
        if (!string.IsNullOrWhiteSpace(demoKey))
            req.Headers.TryAddWithoutValidation("x-cg-demo-api-key", demoKey);

        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        var rows = await JsonSerializer.DeserializeAsync<List<CoinGeckoMarketItem>>(stream, J, ct)
                   ?? new List<CoinGeckoMarketItem>();

        // IMPORTANT: use positional constructor to match your CryptoAsset definition
        var list = rows
            .OrderBy(x => x.market_cap_rank)
            .Select(x => new CryptoAsset(
                x.id,
                x.symbol,
                x.name,
                x.image,
                x.current_price,
                x.market_cap_rank,
                x.price_change_percentage_1y_in_currency,
                x.price_change_percentage_1y_in_currency
            ))
            .ToList();

        _cache.Set(cacheKey, list, TimeSpan.FromMinutes(30));
        return list;
    }

    public async Task<IReadOnlyList<decimal>> GetSparklineAsync(string id, string vsCurrency = "usd", int days = 365, CancellationToken ct = default)
    {
        var cacheKey = $"cg.spark.{id}.{vsCurrency}.{days}";
        if (_cache.TryGetValue(cacheKey, out List<decimal>? cached) && cached is not null)
            return cached;

        var url =
            $"https://api.coingecko.com/api/v3/coins/{id}/market_chart" +
            $"?vs_currency={Uri.EscapeDataString(vsCurrency)}&days={days}&interval=daily";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);

        var demoKey = Environment.GetEnvironmentVariable("COINGECKO_API_KEY");
        if (!string.IsNullOrWhiteSpace(demoKey))
            req.Headers.TryAddWithoutValidation("x-cg-demo-api-key", demoKey);

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Sparkline GET {Url} -> {Code}", url, (int)res.StatusCode);
            return Array.Empty<decimal>();
        }

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("prices", out var pricesEl) ||
            pricesEl.ValueKind != JsonValueKind.Array)
            return Array.Empty<decimal>();

        var list = new List<decimal>(pricesEl.GetArrayLength());
        foreach (var p in pricesEl.EnumerateArray())
        {
            if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 2)
                list.Add(p[1].GetDecimal());
        }

        _cache.Set(cacheKey, list, TimeSpan.FromMinutes(30));
        return list;
    }

    // Minimal DTO for /coins/markets
    private sealed record CoinGeckoMarketItem(
        string id,
        string symbol,
        string name,
        string image,
        decimal current_price,
        int market_cap_rank,
        decimal? price_change_percentage_1y_in_currency
    );
}
