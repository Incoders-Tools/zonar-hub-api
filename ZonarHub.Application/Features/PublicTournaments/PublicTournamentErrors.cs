using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.PublicTournaments;

public static class PublicTournamentErrors
{
    /// <summary>
    /// Unknown, malformed, non-public, inactive and cancelled slugs share this error, so the response never reveals
    /// whether a hidden tournament exists.
    /// </summary>
    public static readonly Error NotFound = Error.NotFound(
        "public_tournaments.not_found",
        "public_tournaments.errors.not_found");

    /// <summary>The showcase tournament has no published draw (none yet, or it is still a private draft).</summary>
    public static readonly Error DrawNotFound = Error.NotFound(
        "public_draws.not_found",
        "public_draws.errors.not_found");
}
