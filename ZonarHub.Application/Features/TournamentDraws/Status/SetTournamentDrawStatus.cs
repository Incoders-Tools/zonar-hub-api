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
/// Idempotent: a draw already in the requested status is returned unchanged, so publishing twice keeps the first
/// publication time.
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
            var updated = await _draws.SetStatusAsync(record.Id, request.TargetStatus, cancellationToken);
            if (updated is null)
                return Result.Failure<TournamentDrawResponse>(TournamentDrawErrors.NotFound);

            record = updated;
        }

        return Result.Success(TournamentDrawScope.ToAdminResponse(
            record, TournamentDrawScope.CurrentResults(record.Structure, snapshot.Results)));
    }
}
