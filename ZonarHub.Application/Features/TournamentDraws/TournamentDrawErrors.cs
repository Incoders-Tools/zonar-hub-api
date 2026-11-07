using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentDraws;

/// <summary>
/// Errors of the admin draw use cases (D1, D5): codes <c>draws.{name}</c>, message keys <c>draws.errors.{name}</c>.
/// Engine and result errors keep their domain codes (<c>DrawErrors</c>, <c>MatchResultErrors</c>).
/// </summary>
public static class TournamentDrawErrors
{
    /// <summary>The tournament has no draw.</summary>
    public static readonly Error NotFound = Error.NotFound("draws.not_found", "draws.errors.not_found");

    /// <summary>A match already has a result other than scheduled: the draw can no longer be replaced or deleted.</summary>
    public static readonly Error ResultsExist = Error.Conflict("draws.results_exist", "draws.errors.results_exist");

    /// <summary>Only a draft can be deleted.</summary>
    public static readonly Error NotDraft = Error.Conflict("draws.not_draft", "draws.errors.not_draft");

    /// <summary>
    /// The draw changed (or was created or deleted) while the request was processed; nothing was written and the client
    /// should read the draw again (D12).
    /// </summary>
    public static readonly Error ConcurrentModification =
        Error.Conflict("draws.concurrent_modification", "draws.errors.concurrent_modification");

    /// <summary>
    /// The result's court id is not one of the tournament's courts (unknown, of another complex or organization, not
    /// selected for the tournament, or inactive when none are selected) (D13).
    /// </summary>
    public static readonly Error InvalidCourt =
        Error.Validation("draws.results.invalid_court", "draws.errors.results.invalid_court");

    /// <summary>The seed is not an unsigned 64-bit decimal string.</summary>
    public static readonly Error InvalidSeed = Error.Validation("draws.invalid_seed", "draws.errors.invalid_seed");
}
