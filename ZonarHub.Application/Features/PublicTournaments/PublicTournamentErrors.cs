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
}
