# CryptoScout

ASP.NET Core 8 website that lists the **top 100 crypto assets** and (optionally) asks an LLM for a **buy shortlist** based on **1-year growth**.
Current stack: **.NET 8 + Razor Pages**, **CoinGecko Demo API** for market data, and **Groq** (OpenAI-compatible) for recommendations.

> Not financial advice. For educational use only.

---

## Highlights

- **Free data source:** CoinGecko Demo API (`coins/markets`)
- **LLM provider:** Groq (`llama-3.1-8b-instant`) via their OpenAI-compatible endpoint
- **Manual AI trigger:** recommendations are generated **only** when you click **“Generate AI Picks”**
- **30-minute tick:** server caches market data for 30 minutes (no websockets)
- **Pagination:** table shows **10 coins per page** with Previous/Next
- **Clean endpoints:** `/api/coins`, `/api/recommend?take=3`, `/health`

---

## Project structure

```
CryptoScout/
├─ CryptoScout.csproj
├─ Program.cs
├─ .env.local              # not committed; holds your API keys
├─ Models/
│  └─ CryptoAsset.cs
├─ Services/
│  ├─ ICryptoDataProvider.cs
│  ├─ CoinGeckoProvider.cs
│  └─ OpenAIRecommender.cs
├─ Pages/
│  ├─ Index.cshtml
│  └─ Index.cshtml.cs
└─ wwwroot/
   └─ (static assets)
```

---

## Prerequisites

- .NET 8 SDK
- A **Groq API key** (free tier available)
- A **CoinGecko Demo API key** (free)

---

## Configuration

Create a file named **`.env.local`** in the project root:

```ini
GROQ_API_KEY=your_groq_key_here
COINGECKO_API_KEY=your_coingecko_demo_key_here
```

Keys are loaded at startup by `DotNetEnv`. Do **not** commit this file.

---

## Install and run

From the project root:

```bash
dotnet restore
dotnet run --urls http://localhost:5000
```

Open `http://localhost:5000`

### Convenience scripts (Windows)

If you saved `run.bat` or `run.ps1` in the root, you can use:

```powershell
.un.bat
# or
powershell -ExecutionPolicy Bypass -File .un.ps1 -Port 5000
```

Both scripts:
- load `.env.local` into the process,
- `dotnet clean` → `dotnet build` → `dotnet run`.

---

## How it works

### Data (CoinGecko)
- `Services/CoinGeckoProvider.cs` calls:
  - `GET /api/v3/coins/markets?vs_currency=usd&per_page=100&order=market_cap_desc&price_change_percentage=1y`
- Sends the header `x-cg-demo-api-key` if `COINGECKO_API_KEY` is present.
- Caches the processed list for **30 minutes**:
  ```csharp
  cache.Set(cacheKey, ordered, TimeSpan.FromMinutes(30));
  ```
  This defines the “tick” cadence. To change it, adjust the value above.

### AI (Groq)
- `Program.cs` builds an `OpenAI.Chat.ChatClient` pointed at `https://api.groq.com/openai/v1` with your `GROQ_API_KEY`.
- `Services/OpenAIRecommender.cs` sends a shortlist of the **top 1-year performers** and asks for a **JSON-only** recommendation (weights + reasoning).
- If the model returns non-JSON or empty results, the code **falls back** to a simple heuristic (top growth, even weights) so the UI always shows picks.

### UI
- `Pages/Index.cshtml`:
  - Loads coins on page load.
  - Filters by name/symbol.
  - Paginates **10 rows per page** (change `const pageSize = 10;` to tweak).
  - **Does not** call the AI automatically. Click **“Generate AI Picks”** to fetch `/api/recommend`.

---

## API endpoints

- `GET /api/coins`  
  Returns `CryptoAsset[]`:
  ```json
  {
    "id": "bitcoin",
    "symbol": "btc",
    "name": "Bitcoin",
    "image": "https://...",
    "currentPrice": 67345.12,
    "marketCapRank": 1,
    "priceChangePercentage1yInCurrency": 120.3,
    "priceChangePercentage1y": 120.3
  }
  ```

- `GET /api/recommend?take=3`  
  Returns:
  ```json
  {
    "top": [
      { "symbol": "btc", "weight": 0.34, "why": "…" },
      { "symbol": "eth", "weight": 0.33, "why": "…" },
      { "symbol": "sol", "weight": 0.33, "why": "…" }
    ],
    "notes": "Risk note…"
  }
  ```

- `GET /health`  
  Returns `{ "ok": true }`.

---

## Customization

- **Change page size**  
  `Pages/Index.cshtml`: `const pageSize = 10;`

- **Change tick interval**  
  `Services/CoinGeckoProvider.cs` cache expiry: `TimeSpan.FromMinutes(30)`.

- **Change model**  
  `Program.cs`: `const string groqModel = "llama-3.1-8b-instant";`

- **Change “take” default**  
  Frontend calls `/api/recommend?take=3`. Adjust the query in `Index.cshtml` or enforce server-side in `Program.cs`.

---

## Troubleshooting

- **Coins load but AI picks say “No picks returned”**  
  The recommender now falls back automatically. If you still see an empty state, check server logs for Groq errors (401/429). Confirm `GROQ_API_KEY` is set and valid.

- **401 or 403 from CoinGecko**  
  Make sure `COINGECKO_API_KEY` is present and you did not exceed Demo limits. The app logs responses when a call fails.

- **.env.local not loaded**  
  Ensure `.env.local` is in the project root and `Program.cs` contains:
  ```csharp
  if (File.Exists(".env.local")) { Env.Load(".env.local"); }
  ```

- **Port conflicts**  
  Run with a different port: `dotnet run --urls http://localhost:5050`

---

## Security

- Never commit secrets. `.env.local` should be git-ignored.
- For production hosting, set environment variables in your platform’s secret manager.

---

## License

You can license this however you prefer. Typical choices are MIT or Apache-2.0.

---

## Disclaimer

Crypto markets are volatile. This project is for demonstration and education only. It does not provide financial advice.
