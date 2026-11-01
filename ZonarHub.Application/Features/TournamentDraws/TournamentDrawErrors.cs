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

    /// <summary>The seed is not an unsigned 64-bit decimal string.</summary>
    public static readonly Error InvalidSeed = Error.Validation("draws.invalid_seed", "draws.errors.invalid_seed");
}
