using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses;

internal static class TournamentStatusCatalogErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "tournament_statuses.not_found",
        "tournament_statuses.errors.not_found");

    public static readonly Error NameRequired = Error.Validation(
        "tournament_statuses.name_required",
        "tournament_statuses.errors.name_required");

    public static readonly Error NameAlreadyExists = Error.Conflict(
        "tournament_statuses.name_exists",
        "tournament_statuses.errors.name_exists");

    public static readonly Error KeyAlreadyExists = Error.Conflict(
        "tournament_statuses.key_exists",
        "tournament_statuses.errors.key_exists");
}
