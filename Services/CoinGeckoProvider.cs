using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using CryptoScout.Models;

namespace CryptoScout.Services;

public sealed class CoinGeckoProvider(HttpClient http, IMemoryCache cache) : ICryptoDataProvider
{
    private const string Base = "https://api.coingecko.com/api/v3";

    public async Task<IReadOnlyList<CryptoAsset>> GetTop100Async(string vsCurrency, CancellationToken ct = default)
    {
        var cacheKey = $"cg-top100-{vsCurrency}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<CryptoAsset>? cached) && cached is not null)
            return cached;

        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"{Base}/coins/markets?vs_currency={Uri.EscapeDataString(vsCurrency)}" +
            "&order=market_cap_desc&per_page=100&page=1" +
            "&price_change_percentage=1y");

        var key = Environment.GetEnvironmentVariable("COINGECKO_API_KEY");
        if (!string.IsNullOrWhiteSpace(key))
            req.Headers.Add("x-cg-demo-api-key", key);

        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, default, ct);

        var list = new List<CryptoAsset>(100);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            string id = el.GetProperty("id").GetString() ?? "";
            string symbol = el.GetProperty("symbol").GetString() ?? "";
            string name = el.GetProperty("name").GetString() ?? "";
            string image = el.TryGetProperty("image", out var im) ? (im.GetString() ?? "") : "";
            decimal price = el.TryGetProperty("current_price", out var cp) && cp.TryGetDecimal(out var p) ? p : 0m;
            int rank = el.TryGetProperty("market_cap_rank", out var r) && r.TryGetInt32(out var rk) ? rk : 0;

            decimal? p1y = TryDec(el, "price_change_percentage_1y_in_currency");

            list.Add(new CryptoAsset(id, symbol, name, image, price, rank, p1y));
        }

        cache.Set(cacheKey, list, TimeSpan.FromMinutes(30));
        return list;
    }

    public async Task<IReadOnlyList<decimal>> GetSparklineAsync(string id, int days, CancellationToken ct = default)
    {
        var cacheKey = $"spark-{id}-{days}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<decimal>? cached) && cached is not null)
            return cached;

        string url = $"{Base}/coins/{Uri.EscapeDataString(id)}/market_chart?vs_currency=usd&days={days}&interval=daily";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        var key = Environment.GetEnvironmentVariable("COINGECKO_API_KEY");
        if (!string.IsNullOrWhiteSpace(key))
            req.Headers.Add("x-cg-demo-api-key", key);

        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, default, ct);

        var arr = doc.RootElement.GetProperty("prices");
        var list = new List<decimal>(arr.GetArrayLength());
        foreach (var p in arr.EnumerateArray())
        {
            if (p.ValueKind == JsonValueKind.Array && p[1].TryGetDecimal(out var v)) list.Add(v);
        }

        cache.Set(cacheKey, list, TimeSpan.FromMinutes(30));
        return list;
    }

    private static decimal? TryDec(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
            if (v.ValueKind == JsonValueKind.Null) return null;
        }
        return null;
    }
}
