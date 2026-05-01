namespace ZonarHub.Infrastructure.ExternalServices;

/// <summary>
/// Configuration bound from <c>ExternalServices:ExchangeRate</c>.
/// </summary>
public sealed class ExchangeRateOptions
{
    public const string SectionName = "ExternalServices:ExchangeRate";

    /// <summary>
    /// Absolute base URL of the exchange rate provider. Must end with '/'.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
}
