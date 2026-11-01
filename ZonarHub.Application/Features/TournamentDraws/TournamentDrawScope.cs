using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Draws;
using ZonarHub.Domain.Draws.Results;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.TournamentDraws;

/// <summary>
/// Shared steps of the admin draw use cases: the tournament tenant rule, reading a stored draw with its results and
/// writing it at the revision that was read (D12).
/// </summary>
internal sealed class TournamentDrawScope
{
    private readonly TournamentOrganizationAccess _access;
    private readonly ITournamentAdminRepository _tournaments;

    public TournamentDrawScope(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments)
    {
        _access = new TournamentOrganizationAccess(currentUser, users, organizations);
        _tournaments = tournaments;
    }

    /// <summary>
    /// The tournament when the caller may manage it: <see cref="AdminTournamentErrors.NotFound"/> for an unknown id,
    /// <see cref="AdminTournamentErrors.Forbidden"/> outside the caller's tenant (same rule as the tournament endpoints).
    /// </summary>
    public async Task<Result<TournamentAdminDto>> LoadTournamentAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        var tournament = await _tournaments.GetByIdAsync(tournamentId, cancellationToken);
        if (tournament is null)
            return Result.Failure<TournamentAdminDto>(AdminTournamentErrors.NotFound);

        if (!await _access.CanAccessAsync(tournament.OrganizationId, cancellationToken))
            return Result.Failure<TournamentAdminDto>(AdminTournamentErrors.Forbidden);

        return Result.Success(tournament);
    }

    /// <summary>
    /// Runs one repository write; a write the store refused becomes its conflict error
    /// (<see cref="TournamentDrawErrors.ConcurrentModification"/>, <see cref="TournamentDrawErrors.ResultsExist"/> or
    /// <see cref="TournamentDrawErrors.NotDraft"/>). Nothing was written in that case.
    /// </summary>
    public static async Task<Result<T>> WriteAsync<T>(Func<Task<T>> write)
    {
        try
        {
            return Result.Success(await write());
        }
        catch (TournamentDrawConcurrencyException refused)
        {
            return Result.Failure<T>(refused.Conflict switch
            {
                TournamentDrawWriteConflict.ResultsExist => TournamentDrawErrors.ResultsExist,
                TournamentDrawWriteConflict.NotDraft => TournamentDrawErrors.NotDraft,
                _ => TournamentDrawErrors.ConcurrentModification,
            });
        }
    }

    /// <summary>
    /// The stored results that fit <paramref name="structure"/>. Replacing a draft keeps its scheduled rows (court or
    /// time only), so a row of a match the new structure does not have (or of a bye) is ignored: it can neither break the
    /// draw nor block replacing it.
    /// </summary>
    public static IReadOnlyList<MatchResult> CurrentResults(Draw structure, IEnumerable<TournamentDrawMatchResult> stored)
    {
        var playable = structure.Phases
            .SelectMany(phase => phase.Matches)
            .Where(match => !match.IsBye)
            .Select(match => match.Id)
            .ToHashSet(StringComparer.Ordinal);

        return stored.Select(row => row.Result).Where(result => playable.Contains(result.MatchId)).ToList();
    }

    /// <summary>Whether any match has started or been decided (D1): scheduled rows (court or time only) do not count.</summary>
    public static bool HasResults(IEnumerable<MatchResult> results) =>
        results.Any(result => result.Status != MatchStatus.Scheduled);

    /// <summary>
    /// Resolves a draw. Stored results were validated with the same rules when recorded, so a failure means the stored
    /// state is corrupt and is surfaced as an exception (500) rather than blamed on the request.
    /// </summary>
    public static ResolvedDraw Resolve(Draw structure, IReadOnlyCollection<MatchResult> results, ScoringRules scoring)
    {
        var resolved = DrawProgression.Resolve(structure, results, scoring);
        return resolved.IsSuccess
            ? resolved.Value
            : throw new InvalidOperationException($"The stored draw cannot be resolved: {resolved.Error.Code}.");
    }

    /// <summary>The admin D7 draw of a stored record with its results.</summary>
    public static TournamentDrawResponse ToAdminResponse(TournamentDrawRecord record, IReadOnlyCollection<MatchResult> results) =>
        TournamentDrawResponseMapper.ToResponse(
            Resolve(record.Structure, results, record.Scoring),
            record.Status,
            record.PublishedAtUtc,
            record.Options,
            TournamentDrawAudience.Admin);
}
