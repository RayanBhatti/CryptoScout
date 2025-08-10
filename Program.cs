using CryptoScout.Services;
using OpenAI.Chat;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<CoinCapProvider>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));

builder.Services.AddScoped<ICryptoDataProvider, CoinCapProvider>();

var openAiKey =
    Environment.GetEnvironmentVariable("OPENAI_API_KEY") ??
    builder.Configuration["OPENAI_API_KEY"] ??
    throw new InvalidOperationException("OPENAI_API_KEY not set");

// official OpenAI SDK ChatClient
builder.Services.AddSingleton(new ChatClient(model: "gpt-4o-mini", apiKey: openAiKey));
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

app.Run();
