using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Draws.Results;

/// <summary>
/// Errors of match results: codes <c>draws.results.{name}</c>, message keys <c>draws.errors.results.{name}</c>.
/// </summary>
public static class MatchResultErrors
{
    public static readonly Error InvalidStatus = Validation("invalid_status");
    public static readonly Error InvalidWinnerSide = Validation("invalid_winner_side");
    public static readonly Error InvalidSet = Validation("invalid_set");
    public static readonly Error InvalidSuperTiebreak = Validation("invalid_super_tiebreak");
    public static readonly Error TooManySets = Validation("too_many_sets");
    public static readonly Error IncompleteScore = Validation("incomplete_score");
    public static readonly Error ScoreDecided = Validation("score_decided");
    public static readonly Error WinnerMismatch = Validation("winner_mismatch");
    public static readonly Error WinnerRequired = Validation("winner_required");
    public static readonly Error WinnerNotAllowed = Validation("winner_not_allowed");
    public static readonly Error SetsNotAllowedForWalkover = Validation("sets_not_allowed_for_walkover");
    public static readonly Error SetsNotAllowedForScheduled = Validation("sets_not_allowed_for_scheduled");
    public static readonly Error InvalidTimes = Validation("invalid_times");

    public static readonly Error UnknownMatch = Error.NotFound(Code("unknown_match"), MessageKey("unknown_match"));
    public static readonly Error DuplicateResult = Conflict("duplicate_result");
    public static readonly Error ByeMatch = Conflict("bye_match");
    public static readonly Error ParticipantsPending = Conflict("participants_pending");
    public static readonly Error LockedByNextMatch = Conflict("locked_by_next_match");

    private static Error Validation(string name) => Error.Validation(Code(name), MessageKey(name));

    private static Error Conflict(string name) => Error.Conflict(Code(name), MessageKey(name));

    private static string Code(string name) => $"draws.results.{name}";

    private static string MessageKey(string name) => $"draws.errors.results.{name}";
}
