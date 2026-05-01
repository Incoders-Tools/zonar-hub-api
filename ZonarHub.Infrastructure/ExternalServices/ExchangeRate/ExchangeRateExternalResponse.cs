namespace ZonarHub.Infrastructure.ExternalServices;

/// <summary>
/// Raw, provider-shaped DTO. Kept <c>internal</c> so it cannot leak past the anti-corruption layer.
/// </summary>
internal sealed class ExchangeRateExternalResponse
{
    public string? Base { get; set; }

    public string? Date { get; set; }

    public Dictionary<string, decimal>? Rates { get; set; }
}
