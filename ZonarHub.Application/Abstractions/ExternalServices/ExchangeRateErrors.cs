using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Abstractions.ExternalServices;

/// <summary>
/// Stable errors surfaced by the exchange rate gateway.
/// </summary>
public static class ExchangeRateErrors
{
    public static readonly Error NotAvailable = Error.NotFound(
        "exchange_rate.not_available",
        "external.exchange_rate.errors.not_available");

    public static readonly Error UpstreamUnavailable = Error.Failure(
        "exchange_rate.upstream_unavailable",
        "external.exchange_rate.errors.upstream_unavailable");
}
