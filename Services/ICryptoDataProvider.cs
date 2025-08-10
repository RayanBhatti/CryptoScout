using CryptoScout.Models;

namespace CryptoScout.Services;

public interface ICryptoDataProvider
{
    Task<IReadOnlyList<CryptoAsset>> GetTop100Async(string vsCurrency = "usd", CancellationToken ct = default);
}
