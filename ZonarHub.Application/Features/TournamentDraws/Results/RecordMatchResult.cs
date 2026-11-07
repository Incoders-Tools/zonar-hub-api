using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.TournamentDraws.Courts;
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
/// A <see cref="MatchResultBody.CourtId"/> must be one of the tournament's <see cref="TournamentDrawCourts"/>, otherwise
/// nothing is written and the request fails with <see cref="TournamentDrawErrors.InvalidCourt"/>; the court's current
/// name replaces any <see cref="MatchResultBody.CourtName"/> sent with it (D13). A court id equal to the one already
/// stored for the match is kept with its stored name without re-validation, so a court later deactivated or removed from
/// the tournament does not block editing the result. Without a court id the free-text name is stored as sent.
/// </remarks>
public sealed class RecordMatchResultHandler
    : IRequestHandler<RecordMatchResultCommand, Result<TournamentDrawResponse>>
{
    private readonly TournamentDrawScope _scope;
    private readonly ITournamentDrawRepository _draws;
    private readonly TournamentDrawCourts _courts;

    public RecordMatchResultHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments,
        ITournamentDrawRepository draws,
        ICourtRepository courts,
        IComplexRepository complexes)
    {
        _scope = new TournamentDrawScope(currentUser, users, organizations, tournaments);
        _draws = draws;
        _courts = new TournamentDrawCourts(courts, complexes);
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

        var mapped = TournamentDrawRequestMapper.ToMatchResult(request.MatchId, request.Body);
        if (mapped.IsFailure)
            return Result.Failure<TournamentDrawResponse>(mapped.Error);

        var result = mapped.Value;
        var stored = snapshot.Results.FirstOrDefault(
            existing => string.Equals(existing.Result.MatchId, request.MatchId, StringComparison.Ordinal));
        if (request.Body.CourtId is { } courtId && stored?.CourtId == courtId)
        {
            result = result with { CourtName = stored.Result.CourtName };
        }
        else if (request.Body.CourtId is { } otherCourtId)
        {
            var court = await _courts.FindAsync(tournament.Value, otherCourtId, cancellationToken);
            if (court is null)
                return Result.Failure<TournamentDrawResponse>(TournamentDrawErrors.InvalidCourt);

            result = result with { CourtName = court.Name };
        }

        var record = snapshot.Draw;
        var current = TournamentDrawScope.CurrentResults(record.Structure, snapshot.Results);
        var resolved = TournamentDrawScope.Resolve(record.Structure, current, record.Scoring);
        var allowed = DrawProgression.CanRecordResult(resolved, request.MatchId, result);
        if (allowed.IsFailure)
            return Result.Failure<TournamentDrawResponse>(allowed.Error);

        var written = await TournamentDrawScope.WriteAsync(
            () => _draws.UpsertResultAsync(record.Version, result, request.Body.CourtId, cancellationToken));
        if (written.IsFailure)
            return Result.Failure<TournamentDrawResponse>(written.Error);

        var updated = snapshot.Results
            .Where(stored => !string.Equals(stored.Result.MatchId, request.MatchId, StringComparison.Ordinal))
            .Append(written.Value.Result)
            .ToList();
        return Result.Success(TournamentDrawScope.ToAdminResponse(record, updated));
    }
}
