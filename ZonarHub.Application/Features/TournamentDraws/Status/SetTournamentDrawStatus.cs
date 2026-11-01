using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.TournamentDraws.Status;

/// <summary>Publishes the draw or returns it to draft, keeping its results (D1).</summary>
public sealed record SetTournamentDrawStatusCommand(Guid TournamentId, TournamentDrawStatus TargetStatus)
    : IRequest<Result<TournamentDrawResponse>>;

/// <remarks>
/// Idempotent: a draw already in the requested status is returned unchanged (nothing is written), so publishing twice
/// keeps the first publication time. The change is written at the revision that was read (D12): when the draw changed
/// or was deleted meanwhile, nothing is written and the request fails with
/// <see cref="TournamentDrawErrors.ConcurrentModification"/>.
/// </remarks>
public sealed class SetTournamentDrawStatusHandler
    : IRequestHandler<SetTournamentDrawStatusCommand, Result<TournamentDrawResponse>>
{
    private readonly TournamentDrawScope _scope;
    private readonly ITournamentDrawRepository _draws;

    public SetTournamentDrawStatusHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments,
        ITournamentDrawRepository draws)
    {
        _scope = new TournamentDrawScope(currentUser, users, organizations, tournaments);
        _draws = draws;
    }

    public async Task<Result<TournamentDrawResponse>> Handle(
        SetTournamentDrawStatusCommand request,
        CancellationToken cancellationToken)
    {
        var tournament = await _scope.LoadTournamentAsync(request.TournamentId, cancellationToken);
        if (tournament.IsFailure)
            return Result.Failure<TournamentDrawResponse>(tournament.Error);

        var snapshot = await _draws.GetByTournamentIdAsync(request.TournamentId, cancellationToken);
        if (snapshot is null)
            return Result.Failure<TournamentDrawResponse>(TournamentDrawErrors.NotFound);

        var record = snapshot.Draw;
        if (record.Status != request.TargetStatus)
        {
            var updated = await TournamentDrawScope.WriteAsync(
                () => _draws.SetStatusAsync(record.Version, request.TargetStatus, cancellationToken));
            if (updated.IsFailure)
                return Result.Failure<TournamentDrawResponse>(updated.Error);

            record = updated.Value;
        }

        return Result.Success(TournamentDrawScope.ToAdminResponse(
            record, TournamentDrawScope.CurrentResults(record.Structure, snapshot.Results)));
    }
}
