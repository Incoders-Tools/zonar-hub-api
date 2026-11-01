using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Draws.Results;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.TournamentDraws.Results;

/// <summary>Records, replaces or clears the result of one match of the tournament's draw (D3, D4).</summary>
public sealed record RecordMatchResultCommand(Guid TournamentId, string MatchId, MatchResultBody Body)
    : IRequest<Result<TournamentDrawResponse>>;

/// <remarks>
/// The result is checked with <see cref="DrawProgression.CanRecordResult"/> against the draw resolved with its stored
/// results, so every <see cref="MatchResultErrors"/> code reaches the client unchanged. Clearing is a
/// <see cref="MatchStatus.Scheduled"/> result without sets or winner: it is subject to the same lock rule as clearing
/// with <c>null</c> and keeps the court and schedule, since the store has no single-result delete. The result is written
/// at the revision the check was made on (D12): when the draw changed meanwhile (for example another result that would
/// lock this match), nothing is written and the request fails with <see cref="TournamentDrawErrors.ConcurrentModification"/>.
/// </remarks>
public sealed class RecordMatchResultHandler
    : IRequestHandler<RecordMatchResultCommand, Result<TournamentDrawResponse>>
{
    private readonly TournamentDrawScope _scope;
    private readonly ITournamentDrawRepository _draws;

    public RecordMatchResultHandler(
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
        RecordMatchResultCommand request,
        CancellationToken cancellationToken)
    {
        var tournament = await _scope.LoadTournamentAsync(request.TournamentId, cancellationToken);
        if (tournament.IsFailure)
            return Result.Failure<TournamentDrawResponse>(tournament.Error);

        var snapshot = await _draws.GetByTournamentIdAsync(request.TournamentId, cancellationToken);
        if (snapshot is null)
            return Result.Failure<TournamentDrawResponse>(TournamentDrawErrors.NotFound);

        var result = TournamentDrawRequestMapper.ToMatchResult(request.MatchId, request.Body);
        if (result.IsFailure)
            return Result.Failure<TournamentDrawResponse>(result.Error);

        var record = snapshot.Draw;
        var current = TournamentDrawScope.CurrentResults(record.Structure, snapshot.Results);
        var resolved = TournamentDrawScope.Resolve(record.Structure, current, record.Scoring);
        var allowed = DrawProgression.CanRecordResult(resolved, request.MatchId, result.Value);
        if (allowed.IsFailure)
            return Result.Failure<TournamentDrawResponse>(allowed.Error);

        var written = await TournamentDrawScope.WriteAsync(
            () => _draws.UpsertResultAsync(record.Version, result.Value, request.Body.CourtId, cancellationToken));
        if (written.IsFailure)
            return Result.Failure<TournamentDrawResponse>(written.Error);

        var updated = current
            .Where(stored => !string.Equals(stored.MatchId, request.MatchId, StringComparison.Ordinal))
            .Append(result.Value)
            .ToList();
        return Result.Success(TournamentDrawScope.ToAdminResponse(record, updated));
    }
}
