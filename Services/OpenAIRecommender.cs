using System.Text.Json;
using CryptoScout.Models;
using OpenAI.Chat;

namespace CryptoScout.Services;

public interface IOpenAIRecommender
{
    Task<RecommendationResult> RecommendAsync(IReadOnlyList<CryptoAsset> assets, int take = 3, CancellationToken ct = default);
}

public sealed class OpenAIRecommender(ChatClient chat) : IOpenAIRecommender
{
    private static readonly JsonSerializerOptions J = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record ShortCoin(string Name, string Symbol, int MarketCapRank, decimal Price, decimal? Change1yPct);

    public async Task<RecommendationResult> RecommendAsync(IReadOnlyList<CryptoAsset> assets, int take = 3, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 10);

        // shortlist: top 20 by 1y% if available
        var ranked = assets
            .Where(a => a.PriceChangePercentage1y is not null)
            .OrderByDescending(a => a.PriceChangePercentage1y)
            .Take(20)
            .Select(a => new ShortCoin(
                a.Name,
                a.Symbol.ToLowerInvariant(),
                a.MarketCapRank,
                a.CurrentPrice,
                a.PriceChangePercentage1y
            ))
            .ToList();

        if (ranked.Count == 0)
        {
            return new RecommendationResult
            {
                Top = [],
                Notes = "No 1-year performance data available from the data source right now."
            };
        }

        var jsonShortlist = JsonSerializer.Serialize(ranked, J);

        var sys = """
        You are a cautious crypto analyst. Recommend coins to *consider* (not financial advice).
        Prefer larger caps with strong 1y growth; avoid low-liquidity memecoins unless top-50 by market cap.
        Output STRICT JSON ONLY. Do not wrap in code fences. Do not include commentary.
        JSON SHAPE:
        {
          "top": [ { "symbol": "btc", "weight": 0.3, "why": "short reason" } ],
          "notes": "1–2 line risk note"
        }
        Weights should sum ~1. Keep "symbol" lowercase. Keep explanations concise.
        """;

        var user = $$"""
        Here is a shortlist (sorted by 1-year growth). Symbols are lowercase:

        {{jsonShortlist}}

        Pick the best {{take}} and justify briefly. Output only JSON per the shape above.
        """;

        string text;
        try
        {
            var completion = await chat.CompleteChatAsync(
                messages:
                [
                    new SystemChatMessage(sys),
                    new UserChatMessage(user)
                ],
                options: new ChatCompletionOptions
                {
                    Temperature = 0.2f
                },
                cancellationToken: ct
            );

            text = completion.Value.Content.FirstOrDefault()?.Text ?? "{}";
        }
        catch (Exception ex)
        {
            return HeuristicFallback(ranked, take, $"LLM error: {ex.Message}");
        }

        // 1) direct parse
        if (TryParse(text, out var parsed) && parsed.Top.Count > 0)
            return parsed;

        // 2) extract first JSON object from any noisy text
        var maybe = ExtractFirstJsonObject(text);
        if (maybe is not null && TryParse(maybe, out var parsed2) && parsed2.Top.Count > 0)
            return parsed2;

        // 3) fallback
        return HeuristicFallback(ranked, take, "Model returned non-JSON or empty picks; using heuristic.");
    }

    private static bool TryParse(string json, out RecommendationResult result)
    {
        try
        {
            result = JsonSerializer.Deserialize<RecommendationResult>(json, J)
                     ?? new RecommendationResult { Top = [], Notes = "Empty JSON." };
            return true;
        }
        catch
        {
            result = new RecommendationResult { Top = [], Notes = "Parse failed." };
            return false;
        }
    }

    private static string? ExtractFirstJsonObject(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        int start = -1, depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '{')
            {
                if (depth == 0) start = i;
                depth++;
            }
            else if (s[i] == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                    return s[start..(i + 1)];
            }
        }
        return null;
    }

    private static RecommendationResult HeuristicFallback(List<ShortCoin> ranked, int take, string reason)
    {
        var picks = ranked.Take(take)
            .Select(rc => new RecommendationResult.Pick
            {
                Symbol = rc.Symbol,
                Weight = 1.0 / take,
                Why = $"Strong 1y growth; consider liquidity and risk. ({rc.Name})"
            })
            .ToList();

        return new RecommendationResult
        {
            Top = picks,
            Notes = $"{reason} Past performance ≠ future results; diversify, use position sizing, and review fundamentals and risk."
        };
    }
}

public sealed class RecommendationResult
{
    public List<Pick> Top { get; set; } = [];
    public string Notes { get; set; } = "";
    public sealed class Pick
    {
        public string Symbol { get; set; } = "";
        public double Weight { get; set; }
        public string Why { get; set; } = "";
    }
}
