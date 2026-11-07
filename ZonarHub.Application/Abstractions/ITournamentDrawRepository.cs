using ZonarHub.Domain.Draws;
using ZonarHub.Domain.Draws.Results;

namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Persistence of the single draw of a tournament (D1) and of its match results (D2). The stored
/// <see cref="TournamentDrawRecord.Structure"/> is the source of truth for the bracket shape: it is never regenerated
/// from the seed after saving.
/// </summary>
/// <remarks>
/// Optimistic concurrency (D12): every stored draw has a <see cref="TournamentDrawRecord.Revision"/> that each write
/// (save, status change, delete, result) increments. Callers read a snapshot, validate it with the domain (no result on
/// save or delete, <see cref="DrawProgression.CanRecordResult"/> before <see cref="UpsertResultAsync"/>), then write
/// with the <see cref="TournamentDrawRecord.Version"/> they read. Each write is one store call and one transaction that
/// locks the draw, checks that version and applies the change, so a write never lands on a draw that changed after the
/// caller's read: it throws <see cref="TournamentDrawConcurrencyException"/>
/// (<see cref="TournamentDrawWriteConflict.ConcurrentModification"/>) instead, and the caller may read again. The store
/// also re-checks the state rules it can see (<see cref="TournamentDrawWriteConflict.ResultsExist"/>,
/// <see cref="TournamentDrawWriteConflict.NotDraft"/>).
/// </remarks>
public interface ITournamentDrawRepository
{
    /// <summary>The tournament's draw, in any status, with every result ordered by match id; null when it has none.</summary>
    Task<TournamentDrawSnapshot?> GetByTournamentIdAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the tournament's draw (revision 0) when <paramref name="expected"/> is null, or replaces the request,
    /// options and structure of the draw at <paramref name="expected"/>, keeping its id and creation time. The saved draw
    /// is always a <see cref="TournamentDrawStatus.Draft"/> without a publication time; stored results are kept.
    /// </summary>
    /// <exception cref="TournamentDrawConcurrencyException">
    /// <see cref="TournamentDrawWriteConflict.ConcurrentModification"/> when the tournament's draw is not at
    /// <paramref name="expected"/> (also when one was created meanwhile), <see cref="TournamentDrawWriteConflict.ResultsExist"/>
    /// when a match has a result other than scheduled.
    /// </exception>
    Task<TournamentDrawRecord> SaveDraftAsync(
        TournamentDrawDraft draft,
        TournamentDrawVersion? expected,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes (publication time = now, kept when already published) or returns the draw to draft (publication time
    /// cleared); results are kept.
    /// </summary>
    /// <exception cref="TournamentDrawConcurrencyException">
    /// <see cref="TournamentDrawWriteConflict.ConcurrentModification"/> when the draw is not at <paramref name="expected"/>
    /// or no longer exists.
    /// </exception>
    Task<TournamentDrawRecord> SetStatusAsync(
        TournamentDrawVersion expected,
        TournamentDrawStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes the draft at <paramref name="expected"/> with its results (cascade).</summary>
    /// <exception cref="TournamentDrawConcurrencyException">
    /// <see cref="TournamentDrawWriteConflict.ConcurrentModification"/> when the draw is not at <paramref name="expected"/>
    /// or no longer exists, <see cref="TournamentDrawWriteConflict.ResultsExist"/> when a match has a result other than
    /// scheduled, <see cref="TournamentDrawWriteConflict.NotDraft"/> when the draw is published.
    /// </exception>
    Task DeleteDraftAsync(TournamentDrawVersion expected, CancellationToken cancellationToken = default);

    /// <summary>Inserts or replaces the result of one match of the draw at <paramref name="expected"/>.</summary>
    /// <exception cref="TournamentDrawConcurrencyException">
    /// <see cref="TournamentDrawWriteConflict.ConcurrentModification"/> when the draw is not at <paramref name="expected"/>
    /// or no longer exists.
    /// </exception>
    Task<RecordedTournamentDrawResult> UpsertResultAsync(
        TournamentDrawVersion expected,
        MatchResult result,
        Guid? courtId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The tournament's draw only when published, with its results; null otherwise. The submitted request and court
    /// ids are not read. The caller still applies the public tournament filters and strips admin-only participant data
    /// (separation keys) from <see cref="PublishedTournamentDraw.Structure"/>.
    /// </summary>
    Task<PublishedTournamentDraw?> GetPublishedByTournamentIdAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default);
}

/// <summary>Visibility of a tournament draw (D1).</summary>
public enum TournamentDrawStatus
{
    /// <summary>Editable and private.</summary>
    Draft = 1,

    /// <summary>Visible publicly when the tournament is public.</summary>
    Published = 2,
}

/// <summary>A draft to save: the request as submitted, the scoring rules and the generated structure.</summary>
/// <param name="Request">Request as submitted (its seed may be null when the API generated one).</param>
/// <param name="Structure">Draw generated from the request; carries the seed actually used.</param>
public sealed record TournamentDrawDraft(
    Guid TournamentId,
    Guid OrganizationId,
    DrawRequest Request,
    ScoringRules Scoring,
    Draw Structure);

/// <summary>A stored draw at one revision: what a write expects to still be current (D12).</summary>
public sealed record TournamentDrawVersion(Guid DrawId, int Revision);

/// <summary>A stored tournament draw. Times are UTC.</summary>
/// <param name="Options">Effective draw options (absent sections are null).</param>
/// <param name="Scoring">Scoring rules the results are validated with.</param>
/// <param name="Revision">Incremented by every write of the draw or of its results (D12).</param>
public sealed record TournamentDrawRecord(
    Guid Id,
    Guid TournamentId,
    Guid OrganizationId,
    TournamentDrawStatus Status,
    Draw Structure,
    DrawRequest Request,
    DrawOptions Options,
    ScoringRules Scoring,
    DateTime? PublishedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int Revision)
{
    /// <summary>The version a write of this record expects.</summary>
    public TournamentDrawVersion Version => new(Id, Revision);
}

/// <summary>A stored draw with its results.</summary>
public sealed record TournamentDrawSnapshot(TournamentDrawRecord Draw, IReadOnlyList<TournamentDrawMatchResult> Results);

/// <summary>A stored match result. <paramref name="CourtId"/> is null when none was set or it was not read.</summary>
public sealed record TournamentDrawMatchResult(MatchResult Result, Guid? CourtId, DateTime UpdatedAtUtc);

/// <summary>A result as stored by <see cref="ITournamentDrawRepository.UpsertResultAsync"/> and the draw's new revision.</summary>
public sealed record RecordedTournamentDrawResult(TournamentDrawMatchResult Result, int Revision);

/// <summary>Why the store refused a draw write.</summary>
public enum TournamentDrawWriteConflict
{
    /// <summary>The draw changed (or was created or deleted) after the caller read it.</summary>
    ConcurrentModification = 1,

    /// <summary>A match has a result other than scheduled.</summary>
    ResultsExist = 2,

    /// <summary>The draw is not a draft.</summary>
    NotDraft = 3,
}

/// <summary>
/// Raised by the <see cref="ITournamentDrawRepository"/> writes when the store refuses the write; nothing was written.
/// </summary>
public sealed class TournamentDrawConcurrencyException(TournamentDrawWriteConflict conflict)
    : Exception($"The tournament draw write was refused: {conflict}.")
{
    public TournamentDrawWriteConflict Conflict { get; } = conflict;
}

/// <summary>Public read of a published draw. Times are UTC.</summary>
/// <param name="Options">Effective draw options (absent sections are null).</param>
public sealed record PublishedTournamentDraw(
    Guid TournamentId,
    Draw Structure,
    DrawOptions Options,
    ScoringRules Scoring,
    DateTime PublishedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<TournamentDrawMatchResult> Results);
