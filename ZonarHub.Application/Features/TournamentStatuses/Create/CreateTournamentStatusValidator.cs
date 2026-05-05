using FluentValidation;

namespace ZonarHub.Application.Features.TournamentStatuses.Create;

internal sealed class CreateTournamentStatusValidator : AbstractValidator<CreateTournamentStatusCommand>
{
    public CreateTournamentStatusValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("tournament_statuses.errors.name_required")
            .MaximumLength(120);

        RuleFor(x => x.Key)
            .NotEmpty()
            .WithMessage("tournament_statuses.errors.key_required")
            .Matches("^[a-z0-9_]+$")
            .WithMessage("tournament_statuses.errors.key_pattern")
            .MaximumLength(80);

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0)
            .WithMessage("tournament_statuses.errors.sort_order_min");
    }
}
