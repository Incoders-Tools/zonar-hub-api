using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Tournaments;

public static class TournamentErrors
{
    public static readonly Error NotFound = Error.NotFound("tournament.not_found", "tournaments.errors.not_found");
    public static readonly Error NameRequired = Error.Validation("tournament.name_required", "tournaments.errors.name_required");
    public static readonly Error OrganizationIdRequired = Error.Validation("tournament.organization_id_required", "tournaments.errors.organization_id_required");
    public static readonly Error SportIdRequired = Error.Validation("tournament.sport_id_required", "tournaments.errors.sport_id_required");
    public static readonly Error InvalidDateRange = Error.Validation("tournament.invalid_date_range", "tournaments.errors.invalid_date_range");
}
