using ZonarHub.Domain.Draws;
using ZonarHub.Domain.Draws.Results;

namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Persistence of the single draw of a tournament (D1) and of its match results (D2). The stored
/// <see cref="TournamentDrawRecord.Structure"/> is the source of truth for the bracket shape: it is never regenerated
/// from the seed after saving.
/// </summary>
/// <remarks>
/// Every method is one atomic store call. Rules that span calls are the caller's: saving or deleting a draft only
/// while no match has a result, and <see cref="DrawProgression.CanRecordResult"/> before
/// <see cref="UpsertResultAsync"/>. Those checks are read-modify-write races: a result recorded between the caller's
/// read and a <see cref="SaveDraftAsync"/> stays attached to the replaced draw (its match id may no longer exist), a
/// <see cref="DeleteDraftAsync"/> removes it with the draw, and two concurrent results can both pass the lock check of
/// the next match. Callers that need stronger guarantees must serialize writes per draw.
/// </remarks>
public interface ITournamentDrawRepository
{
    /// <summary>The tournament's draw, in any status, with every result ordered by match id; null when it has none.</summary>
    Task<TournamentDrawSnapshot?> GetByTournamentIdAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the tournament's draw or replaces its request, options and structure, keeping its id and creation time.
    /// The saved draw is always a <see cref="TournamentDrawStatus.Draft"/> without a publication time.
    /// </summary>
    Task<TournamentDrawRecord> SaveDraftAsync(TournamentDrawDraft draft, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes (publication time = now) or returns the draw to draft (publication time cleared); results are kept.
    /// Null when no draw has <paramref name="drawId"/>.
    /// </summary>
    Task<TournamentDrawRecord?> SetStatusAsync(
        Guid drawId,
        TournamentDrawStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the draw only while it is a draft, with its results (cascade). False when no draft has
    /// <paramref name="drawId"/>, including when the draw is published.
    /// </summary>
    Task<bool> DeleteDraftAsync(Guid drawId, CancellationToken cancellationToken = default);

    /// <summary>Inserts or replaces the result of one match of the draw (one upsert on draw and match id).</summary>
    Task<TournamentDrawMatchResult> UpsertResultAsync(
        Guid drawId,
        MatchResult result,
        Guid? courtId,
        CancellationToken cancellationToken = default);

    /// <summary>Every result of the draw, ordered by match id.</summary>
    Task<IReadOnlyList<TournamentDrawMatchResult>> ListResultsAsync(Guid drawId, CancellationToken cancellationToken = default);

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

/// <summary>A stored tournament draw. Times are UTC.</summary>
/// <param name="Options">Effective draw options (absent sections are null).</param>
/// <param name="Scoring">Scoring rules the results are validated with.</param>
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
    DateTime UpdatedAtUtc);

/// <summary>A stored draw with its results.</summary>
public sealed record TournamentDrawSnapshot(TournamentDrawRecord Draw, IReadOnlyList<TournamentDrawMatchResult> Results);

/// <summary>A stored match result. <paramref name="CourtId"/> is null when none was set or it was not read.</summary>
public sealed record TournamentDrawMatchResult(MatchResult Result, Guid? CourtId, DateTime UpdatedAtUtc);

/// <summary>Public read of a published draw. Times are UTC.</summary>
public sealed record PublishedTournamentDraw(
    Guid TournamentId,
    Draw Structure,
    ScoringRules Scoring,
    DateTime PublishedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<TournamentDrawMatchResult> Results);
