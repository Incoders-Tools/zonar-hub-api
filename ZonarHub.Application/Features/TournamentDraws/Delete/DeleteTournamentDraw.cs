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
/// <see cref="TournamentDrawErrors.NotDraft"/> when the draw is published. The delete is written at the revision those
/// checks read (D12): when the draw changed meanwhile (a result recorded, published, deleted), nothing is deleted and
/// the request fails with <see cref="TournamentDrawErrors.ConcurrentModification"/>.
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

        if (TournamentDrawScope.HasResults(TournamentDrawScope.CurrentResults(snapshot.Draw.Structure, snapshot.Results)))
            return Result.Failure(TournamentDrawErrors.ResultsExist);

        if (snapshot.Draw.Status != TournamentDrawStatus.Draft)
            return Result.Failure(TournamentDrawErrors.NotDraft);

        var deleted = await TournamentDrawScope.WriteAsync(async () =>
        {
            await _draws.DeleteDraftAsync(snapshot.Draw.Version, cancellationToken);
            return true;
        });
        return deleted.IsSuccess ? Result.Success() : Result.Failure(deleted.Error);
    }
}
