namespace ZonarHub.Application.Abstractions.ExternalServices;

/// <summary>
/// Internal, provider-agnostic representation of an exchange rate produced by the anti-corruption layer.
/// </summary>
public sealed record ExchangeRate(
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    DateTime RetrievedAtUtc);
