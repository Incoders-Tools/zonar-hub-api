using ZonarHub.Application.Abstractions.ExternalServices;

namespace ZonarHub.Application.Features.External.GetExchangeRate;

/// <summary>
/// Outward-facing exchange rate payload returned by the API.
/// </summary>
public sealed record ExchangeRateResponse(
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    DateTime RetrievedAtUtc)
{
    public static ExchangeRateResponse FromDomain(ExchangeRate rate) =>
        new(rate.BaseCurrency, rate.QuoteCurrency, rate.Rate, rate.RetrievedAtUtc);
}
