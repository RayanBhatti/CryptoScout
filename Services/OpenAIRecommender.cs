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
    public async Task<RecommendationResult> RecommendAsync(IReadOnlyList<CryptoAsset> assets, int take = 3, CancellationToken ct = default)
    {
        var topGrowth = assets
            .Where(a => a.PriceChangePercentage1y is not null)
            .OrderByDescending(a => a.PriceChangePercentage1y)
            .Take(20)
            .Select(a => new
            {
                a.Name,
                a.Symbol,
                a.MarketCapRank,
                Price = a.CurrentPrice,
                Change1yPct = a.PriceChangePercentage1y
            });

        var jsonShortlist = JsonSerializer.Serialize(topGrowth);

        var sys = """
        You are a cautious crypto analyst. Recommend coins to *consider* buying (not financial advice).
        Prefer larger caps with strong 1y growth, but beware volatility and survivorship bias.
        Return STRICT JSON only: { "top": [ { "symbol": "", "weight": 0-1, "why": "" } ], "notes": "" }.
        Weights should sum roughly to 1. Avoid memecoins unless top-50 by market cap.
        """;

        var user = $$"""
        Shortlist (sorted by 1-year growth). Symbols are lowercase:

        {{jsonShortlist}}

        Pick the best {{take}} and justify briefly. Include a risk note.
        """;

        var completion = await chat.CompleteChatAsync(
            messages:
            [
                new SystemChatMessage(sys),
                new UserChatMessage(user)
            ],
            options: new ChatCompletionOptions
            {
                Temperature = 0.2f,
                // You can also set: MaxOutputTokens, TopP, etc.
            },
            cancellationToken: ct
        );

        var first = completion.Value.Content.FirstOrDefault();
        var text = first?.Text ?? "{}";

        try
        {
            var parsed = JsonSerializer.Deserialize<RecommendationResult>(text);
            return parsed ?? new RecommendationResult
            {
                Top = [],
                Notes = "Failed to parse model output."
            };
        }
        catch
        {
            return new RecommendationResult
            {
                Top = [],
                Notes = "Model returned non-JSON; try again."
            };
        }
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
