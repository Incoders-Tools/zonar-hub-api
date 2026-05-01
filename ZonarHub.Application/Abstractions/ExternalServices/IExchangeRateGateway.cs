using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Abstractions.ExternalServices;

/// <summary>
/// Anti-corruption boundary for the external exchange rate provider.
/// Implementations are responsible for HTTP access, resilience, and translating
/// provider-shaped payloads into the internal <see cref="ExchangeRate"/> model.
/// </summary>
public interface IExchangeRateGateway
{
    Task<Result<ExchangeRate>> GetAsync(
        string baseCurrency,
        string quoteCurrency,
        CancellationToken cancellationToken = default);
}
