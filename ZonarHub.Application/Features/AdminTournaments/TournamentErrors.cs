using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminTournaments;

internal static class AdminTournamentErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "tournaments.not_found",
        "tournaments.errors.not_found");

    public static readonly Error OrganizationRequired = Error.Validation(
        "tournaments.organization_required",
        "tournaments.errors.organization_required");

    public static readonly Error NameRequired = Error.Validation(
        "tournaments.name_required",
        "tournaments.errors.name_required");

    public static readonly Error SportRequired = Error.Validation(
        "tournaments.sport_required",
        "tournaments.errors.sport_required");

    public static readonly Error InvalidDateRange = Error.Validation(
        "tournaments.invalid_date_range",
        "tournaments.errors.invalid_date_range");
}
