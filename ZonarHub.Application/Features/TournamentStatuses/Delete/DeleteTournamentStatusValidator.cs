using FluentValidation;

namespace ZonarHub.Application.Features.TournamentStatuses.Delete;

internal sealed class DeleteTournamentStatusValidator : AbstractValidator<DeleteTournamentStatusCommand>
{
    public DeleteTournamentStatusValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("tournament_statuses.errors.id_required");
    }
}
