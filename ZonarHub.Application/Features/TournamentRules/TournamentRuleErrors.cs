using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentRules;

internal static class TournamentRuleErrors
{
    public static readonly Error NameRequired = Error.Validation(
        "tournament_rules.name_required",
        "tournament_rules.errors.name_required");

    public static readonly Error NotFound = Error.NotFound(
        "tournament_rules.not_found",
        "tournament_rules.errors.not_found");
}
