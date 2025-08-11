using CryptoScout.Services;
using OpenAI;               // OpenAIClientOptions
using OpenAI.Chat;         // ChatClient, messages, options
using System.ClientModel;  // ApiKeyCredential
using DotNetEnv;           // Env.Load
using Microsoft.Extensions.Caching.Memory; // IMemoryCache
using System.Text.Json;

// Load .env.local if present (so GROQ_API_KEY / COINGECKO_API_KEY are available)
if (File.Exists(".env.local"))
{
    Env.Load(".env.local");
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();

// Data provider: CoinGecko (free Demo API)
builder.Services.AddHttpClient<CoinGeckoProvider>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));
builder.Services.AddScoped<ICryptoDataProvider, CoinGeckoProvider>();

// ==== GROQ (OpenAI-compatible) ====
var groqKey =
    Environment.GetEnvironmentVariable("GROQ_API_KEY")
    ?? builder.Configuration["GROQ_API_KEY"]
    ?? throw new InvalidOperationException("GROQ_API_KEY not set.");

const string groqModel = "llama-3.1-8b-instant";

builder.Services.AddSingleton(new ChatClient(
    model: groqModel,
    credential: new ApiKeyCredential(groqKey),
    options: new OpenAIClientOptions
    {
        Endpoint = new Uri("https://api.groq.com/openai/v1")
    }
));

builder.Services.AddSingleton<IOpenAIRecommender, OpenAIRecommender>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();

// ========== API endpoints ==========

// Coins (server cache handled inside provider for 30 minutes)
app.MapGet("/api/coins", async (ICryptoDataProvider provider, CancellationToken ct) =>
{
    var data = await provider.GetTop100Async("usd", ct);
    return Results.Ok(data);
});

// Recommend (also caches the latest recommendation for chat context)
app.MapGet("/api/recommend", async (
    ICryptoDataProvider provider,
    IOpenAIRecommender rec,
    IMemoryCache cache,
    int take,
    CancellationToken ct) =>
{
    var data = await provider.GetTop100Async("usd", ct);
    var result = await rec.RecommendAsync(data, take == 0 ? 3 : Math.Clamp(take, 1, 10), ct);

    // Save for chat context (expires with the market data cadence)
    cache.Set("last_reco", result, TimeSpan.FromMinutes(30));

    return Results.Ok(result);
});

// 1y sparkline for a specific coin id (e.g., "bitcoin")
app.MapGet("/api/sparkline", async (ICryptoDataProvider provider, string id, int days, CancellationToken ct) =>
{
    var d = days <= 0 ? 365 : days;
    var values = await provider.GetSparklineAsync(id, d, ct); // <-- removed "usd"
    return Results.Ok(values);
});


// Chat about the latest picks
app.MapPost("/api/chat", async (
    ChatClient chat,
    IMemoryCache cache,
    ChatRequest req,
    CancellationToken ct) =>
{
    var latest = cache.Get<CryptoScout.Services.RecommendationResult>("last_reco");
    if (latest is null)
    {
        return Results.Ok(new ChatResponse(new()
        {
            new ChatMessageDto("assistant", "I don’t have any picks yet. Click “Generate AI Picks” first, then ask me anything about them.")
        }));
    }

    // Give the model the current picks + guidance to stay on-topic and safe
    var contextJson = JsonSerializer.Serialize(latest);
    var sys = $"""
    You are a helpful crypto assistant. The user will ask about the latest shortlist below.
    Stay on-topic, be concise, and do not give financial advice. If asked to place trades, politely refuse.
    SHORTLIST JSON:
    {contextJson}
    """;

    var messages = new List<ChatMessage> { new SystemChatMessage(sys) };

    // Append prior conversation history from the client
    foreach (var m in (req.messages ?? new()))
    {
        var content = m.content ?? "";
        if (string.Equals(m.role, "assistant", StringComparison.OrdinalIgnoreCase))
            messages.Add(new AssistantChatMessage(content));
        else
            messages.Add(new UserChatMessage(content));
    }

    var completion = await chat.CompleteChatAsync(
        messages: messages,
        options: new ChatCompletionOptions
        {
            Temperature = 0.2f
        },
        cancellationToken: ct
    );

    var text = completion.Value.Content.FirstOrDefault()?.Text ?? "(no reply)";
    return Results.Ok(new ChatResponse(new()
    {
        new ChatMessageDto("assistant", text)
    }));
});

app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.Run();

// ====== DTOs for chat API ======
public sealed record ChatRequest(List<ChatMessageDto>? messages);
public sealed record ChatMessageDto(string role, string content);
public sealed record ChatResponse(List<ChatMessageDto> messages);

// Required for WebApplicationFactory in integration tests (harmless in prod)
public partial class Program { }
