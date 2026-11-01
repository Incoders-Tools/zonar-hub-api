using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.TournamentDraws.Delete;

/// <summary>Deletes the tournament's draft draw while no match has a result (D1).</summary>
public sealed record DeleteTournamentDrawCommand(Guid TournamentId) : IRequest<Result>;

/// <remarks>
/// <see cref="TournamentDrawErrors.ResultsExist"/> when a match has a result other than scheduled,
/// <see cref="TournamentDrawErrors.NotDraft"/> when the draw is published (also when it was published or deleted between
/// the read and the delete). Results are read again right before deleting; a result recorded after that read is
/// deleted with the draw (residual race, see <see cref="ITournamentDrawRepository"/>).
/// </remarks>
public sealed class DeleteTournamentDrawHandler : IRequestHandler<DeleteTournamentDrawCommand, Result>
{
    private readonly TournamentDrawScope _scope;
    private readonly ITournamentDrawRepository _draws;

    public DeleteTournamentDrawHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments,
        ITournamentDrawRepository draws)
    {
        _scope = new TournamentDrawScope(currentUser, users, organizations, tournaments);
        _draws = draws;
    }

    public async Task<Result> Handle(DeleteTournamentDrawCommand request, CancellationToken cancellationToken)
    {
        var tournament = await _scope.LoadTournamentAsync(request.TournamentId, cancellationToken);
        if (tournament.IsFailure)
            return Result.Failure(tournament.Error);

        var snapshot = await _draws.GetByTournamentIdAsync(request.TournamentId, cancellationToken);
        if (snapshot is null)
            return Result.Failure(TournamentDrawErrors.NotFound);

        var structure = snapshot.Draw.Structure;
        if (TournamentDrawScope.HasResults(TournamentDrawScope.CurrentResults(structure, snapshot.Results)))
            return Result.Failure(TournamentDrawErrors.ResultsExist);

        if (snapshot.Draw.Status != TournamentDrawStatus.Draft)
            return Result.Failure(TournamentDrawErrors.NotDraft);

        var latest = await _draws.ListResultsAsync(snapshot.Draw.Id, cancellationToken);
        if (TournamentDrawScope.HasResults(TournamentDrawScope.CurrentResults(structure, latest)))
            return Result.Failure(TournamentDrawErrors.ResultsExist);

        return await _draws.DeleteDraftAsync(snapshot.Draw.Id, cancellationToken)
            ? Result.Success()
            : Result.Failure(TournamentDrawErrors.NotDraft);
    }
}
