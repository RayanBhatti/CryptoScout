using CryptoScout.Services;
using OpenAI;        // OpenAIClientOptions
using OpenAI.Chat;  // ChatClient
using System.ClientModel; // ApiKeyCredential
using DotNetEnv;

Env.Load(".env.local");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<CoinCapProvider>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));

builder.Services.AddScoped<ICryptoDataProvider, CoinCapProvider>();

// ==== DeepInfra only ====
var apiKey =
    Environment.GetEnvironmentVariable("DEEPINFRA_API_KEY")
    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? builder.Configuration["DEEPINFRA_API_KEY"]
    ?? builder.Configuration["OPENAI_API_KEY"]
    ?? throw new InvalidOperationException("DEEPINFRA_API_KEY not set.");

const string model = "meta-llama/Meta-Llama-3-8B-Instruct";

builder.Services.AddSingleton(new ChatClient(
    model: model,
    credential: new ApiKeyCredential(apiKey),
    options: new OpenAIClientOptions
    {
        Endpoint = new Uri("https://api.deepinfra.com/v1/openai/")
    }
)); // Using custom base URL via ApiKeyCredential + OpenAIClientOptions. :contentReference[oaicite:1]{index=1}

builder.Services.AddSingleton<IOpenAIRecommender, OpenAIRecommender>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();

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
