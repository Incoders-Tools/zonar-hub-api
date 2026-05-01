using FluentValidation;

namespace ZonarHub.Application.Features.External.GetExchangeRate;

public sealed class GetExchangeRateValidator : AbstractValidator<GetExchangeRateQuery>
{
    public GetExchangeRateValidator()
    {
        RuleFor(x => x.BaseCurrency)
            .NotEmpty().WithMessage("external.exchange_rate.errors.base_currency_required")
            .Matches("^[A-Za-z]{3}$").WithMessage("external.exchange_rate.errors.currency_invalid");

        RuleFor(x => x.QuoteCurrency)
            .NotEmpty().WithMessage("external.exchange_rate.errors.quote_currency_required")
            .Matches("^[A-Za-z]{3}$").WithMessage("external.exchange_rate.errors.currency_invalid");
    }
}
