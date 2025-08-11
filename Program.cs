using CryptoScout.Services;
using OpenAI;               // OpenAIClientOptions
using OpenAI.Chat;         // ChatClient
using System.ClientModel;  // ApiKeyCredential
using DotNetEnv;           // Env.Load

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

// ==== GROQ (OpenAI-compatible; free tier) ====
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

// API endpoints (server cache in provider is 30 min)
app.MapGet("/api/coins", async (ICryptoDataProvider provider, CancellationToken ct) =>
{
    var data = await provider.GetTop100Async("usd", ct);
    return Results.Ok(data);
});

app.MapGet("/api/recommend", async (ICryptoDataProvider provider, IOpenAIRecommender rec, int take, CancellationToken ct) =>
{
    var data = await provider.GetTop100Async("usd", ct);
    var r = await rec.RecommendAsync(data, take == 0 ? 3 : Math.Clamp(take, 1, 10), ct);
    return Results.Ok(r);
});

app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.Run();