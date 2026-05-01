using System.Net.Http.Json;
using System.Text.Json;

namespace ZonarHub.Infrastructure.ExternalServices;

/// <summary>
/// Typed HTTP client for the external exchange rate provider.
/// Exposes only DTO-shaped results; translation to the application model happens in the gateway.
/// </summary>
internal sealed class ExchangeRateApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;

    public ExchangeRateApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ExchangeRateExternalResponse?> GetLatestAsync(
        string baseCurrency,
        string quoteCurrency,
        CancellationToken cancellationToken)
    {
        var url = $"latest?base={Uri.EscapeDataString(baseCurrency)}&symbols={Uri.EscapeDataString(quoteCurrency)}";
        return await _httpClient.GetFromJsonAsync<ExchangeRateExternalResponse>(url, JsonOptions, cancellationToken);
    }
}
