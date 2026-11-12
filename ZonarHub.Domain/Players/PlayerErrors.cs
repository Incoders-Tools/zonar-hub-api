using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Players;

/// <summary>
/// Player errors. The store codes (<see cref="EmailTaken"/>, <see cref="DocumentTaken"/>, <see cref="UserTaken"/>) come
/// from the unique indexes of migration <c>20261110100000_tournament_registrations</c>.
/// </summary>
public static class PlayerErrors
{
    public static readonly Error NotFound = Error.NotFound("players.not_found", "players.errors.not_found");
    public static readonly Error OrganizationIdRequired = Error.Validation("players.organization_id_required", "players.errors.organization_id_required");
    public static readonly Error FirstNameRequired = Error.Validation("players.first_name_required", "players.errors.first_name_required");
    public static readonly Error LastNameRequired = Error.Validation("players.last_name_required", "players.errors.last_name_required");
    public static readonly Error NameTooLong = Error.Validation("players.name_too_long", "players.errors.name_too_long");
    public static readonly Error InvalidEmail = Error.Validation("players.invalid_email", "players.errors.invalid_email");
    public static readonly Error FieldTooLong = Error.Validation("players.field_too_long", "players.errors.field_too_long");
    public static readonly Error BirthDateInFuture = Error.Validation("players.birth_date_in_future", "players.errors.birth_date_in_future");
    public static readonly Error UnknownGender = Error.Validation("players.unknown_gender", "players.errors.unknown_gender");
    public static readonly Error UnknownCategory = Error.Validation("players.unknown_category", "players.errors.unknown_category");
    public static readonly Error EmailTaken = Error.Conflict("players.email_taken", "players.errors.email_taken");
    public static readonly Error DocumentTaken = Error.Conflict("players.document_taken", "players.errors.document_taken");
    public static readonly Error UserTaken = Error.Conflict("players.user_taken", "players.errors.user_taken");
}
