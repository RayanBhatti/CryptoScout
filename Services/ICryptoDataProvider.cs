using CryptoScout.Models;

namespace CryptoScout.Services
{
    public interface ICryptoDataProvider
    {
        Task<IReadOnlyList<CryptoAsset>> GetTop100Async(string vsCurrency, CancellationToken ct = default);
        Task<IReadOnlyList<decimal>> GetSparklineAsync(string id, int days, CancellationToken ct = default);
    }
}
