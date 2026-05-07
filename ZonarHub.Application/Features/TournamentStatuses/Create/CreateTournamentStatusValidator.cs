using FluentValidation;

namespace ZonarHub.Application.Features.TournamentStatuses.Create;

internal sealed class CreateTournamentStatusValidator : AbstractValidator<CreateTournamentStatusCommand>
{
    public CreateTournamentStatusValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty()
            .WithMessage("tournament_statuses.errors.key_required")
            .Matches("^[a-z0-9_]+$")
            .WithMessage("tournament_statuses.errors.key_pattern")
            .MaximumLength(80);

        // At least one localized name has to be present so the row is valid.
        // The handler mirrors the supplied locale into the missing ones to
        // satisfy the NOT NULL columns.
        RuleFor(x => x)
            .Must(x => HasAtLeastOneName(x))
            .WithMessage("tournament_statuses.errors.name_required");

        RuleFor(x => x.NameEs).MaximumLength(120);
        RuleFor(x => x.NameEn).MaximumLength(120);
        RuleFor(x => x.NamePt).MaximumLength(120);

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0)
            .WithMessage("tournament_statuses.errors.sort_order_min");
    }

    private static bool HasAtLeastOneName(CreateTournamentStatusCommand command)
        => !string.IsNullOrWhiteSpace(command.NameEs)
            || !string.IsNullOrWhiteSpace(command.NameEn)
            || !string.IsNullOrWhiteSpace(command.NamePt);
}
