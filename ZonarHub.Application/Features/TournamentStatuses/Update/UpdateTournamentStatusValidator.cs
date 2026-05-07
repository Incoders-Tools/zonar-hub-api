using FluentValidation;

namespace ZonarHub.Application.Features.TournamentStatuses.Update;

internal sealed class UpdateTournamentStatusValidator : AbstractValidator<UpdateTournamentStatusCommand>
{
    public UpdateTournamentStatusValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("tournament_statuses.errors.id_required");

        RuleFor(x => x.NameEs).MaximumLength(120);
        RuleFor(x => x.NameEn).MaximumLength(120);
        RuleFor(x => x.NamePt).MaximumLength(120);

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0)
            .WithMessage("tournament_statuses.errors.sort_order_min");
    }
}
