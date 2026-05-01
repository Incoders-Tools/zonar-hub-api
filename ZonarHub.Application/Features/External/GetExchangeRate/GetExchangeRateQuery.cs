using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.External.GetExchangeRate;

public sealed record GetExchangeRateQuery(string BaseCurrency, string QuoteCurrency)
    : IRequest<Result<ExchangeRateResponse>>;
