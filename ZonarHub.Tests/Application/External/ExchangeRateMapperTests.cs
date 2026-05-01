using ZonarHub.Infrastructure.ExternalServices;

namespace ZonarHub.Tests.Application.External;

public class ExchangeRateMapperTests
{
    private static readonly DateTime Now = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ToApplicationModel_WithMatchingQuote_ProducesUppercasedModel()
    {
        var dto = new ExchangeRateExternalResponse
        {
            Base = "USD",
            Date = "2026-04-24",
            Rates = new Dictionary<string, decimal> { ["EUR"] = 0.93m },
        };

        var mapped = ExchangeRateMapper.ToApplicationModel(dto, "usd", "eur", Now);

        Assert.NotNull(mapped);
        Assert.Equal("USD", mapped!.BaseCurrency);
        Assert.Equal("EUR", mapped.QuoteCurrency);
        Assert.Equal(0.93m, mapped.Rate);
        Assert.Equal(Now, mapped.RetrievedAtUtc);
    }

    [Fact]
    public void ToApplicationModel_WhenQuoteMissing_ReturnsNull()
    {
        var dto = new ExchangeRateExternalResponse
        {
            Rates = new Dictionary<string, decimal> { ["GBP"] = 0.82m },
        };

        var mapped = ExchangeRateMapper.ToApplicationModel(dto, "USD", "EUR", Now);

        Assert.Null(mapped);
    }

    [Fact]
    public void ToApplicationModel_WhenDtoNull_ReturnsNull()
    {
        var mapped = ExchangeRateMapper.ToApplicationModel(null, "USD", "EUR", Now);
        Assert.Null(mapped);
    }
}
