using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Abstractions.ExternalServices;
using ZonarHub.Domain.Common;
using Microsoft.Extensions.Logging;

namespace ZonarHub.Infrastructure.ExternalServices;

/// <summary>
/// Anti-corruption implementation of <see cref="IExchangeRateGateway"/>.
/// Orchestrates the typed client and mapper and absorbs provider-side failures.
/// </summary>
internal sealed class ExchangeRateGateway : IExchangeRateGateway
{
    private readonly ExchangeRateApiClient _client;
    private readonly IClock _clock;
    private readonly ILogger<ExchangeRateGateway> _logger;

    public ExchangeRateGateway(
        ExchangeRateApiClient client,
        IClock clock,
        ILogger<ExchangeRateGateway> logger)
    {
        _client = client;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<ExchangeRate>> GetAsync(
        string baseCurrency,
        string quoteCurrency,
        CancellationToken cancellationToken = default)
    {
        var normalizedBase = baseCurrency.Trim().ToUpperInvariant();
        var normalizedQuote = quoteCurrency.Trim().ToUpperInvariant();

        try
        {
            var dto = await _client.GetLatestAsync(normalizedBase, normalizedQuote, cancellationToken);
            var mapped = ExchangeRateMapper.ToApplicationModel(dto, normalizedBase, normalizedQuote, _clock.UtcNow);
            if (mapped is null)
            {
                return Result.Failure<ExchangeRate>(ExchangeRateErrors.NotAvailable);
            }

            return Result.Success(mapped);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Exchange rate provider request failed for {Base}->{Quote}", normalizedBase, normalizedQuote);
            return Result.Failure<ExchangeRate>(ExchangeRateErrors.UpstreamUnavailable);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Exchange rate provider timed out for {Base}->{Quote}", normalizedBase, normalizedQuote);
            return Result.Failure<ExchangeRate>(ExchangeRateErrors.UpstreamUnavailable);
        }
    }
}
