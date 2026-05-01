using ZonarHub.Application.Abstractions.ExternalServices;

namespace ZonarHub.Infrastructure.ExternalServices;

/// <summary>
/// Converts the provider DTO into the internal <see cref="ExchangeRate"/> application model.
/// </summary>
internal static class ExchangeRateMapper
{
    public static ExchangeRate? ToApplicationModel(
        ExchangeRateExternalResponse? dto,
        string requestedBaseCurrency,
        string requestedQuoteCurrency,
        DateTime retrievedAtUtc)
    {
        if (dto?.Rates is null)
        {
            return null;
        }

        if (!TryGetRate(dto.Rates, requestedQuoteCurrency, out var rate))
        {
            return null;
        }

        return new ExchangeRate(
            requestedBaseCurrency.ToUpperInvariant(),
            requestedQuoteCurrency.ToUpperInvariant(),
            rate,
            retrievedAtUtc);
    }

    private static bool TryGetRate(Dictionary<string, decimal> rates, string quoteCurrency, out decimal rate)
    {
        foreach (var pair in rates)
        {
            if (string.Equals(pair.Key, quoteCurrency, StringComparison.OrdinalIgnoreCase))
            {
                rate = pair.Value;
                return true;
            }
        }

        rate = default;
        return false;
    }
}
