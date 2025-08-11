using CryptoScout.Models;

namespace CryptoScout.Services;

public interface ICryptoDataProvider
{
    Task<IReadOnlyList<CryptoAsset>> GetTop100Async(string vsCurrency = "usd", CancellationToken ct = default);

    Task<IReadOnlyList<decimal>> GetSparklineAsync(string id, string vsCurrency = "usd", int days = 365, CancellationToken ct = default);
}
