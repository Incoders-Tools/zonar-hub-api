using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Users;

public static class UserErrors
{
    public static readonly Error NotFound = Error.NotFound("user.not_found", "users.errors.not_found");
    public static readonly Error EmailRequired = Error.Validation("user.email_required", "users.errors.email_required");
    public static readonly Error EmailAlreadyExists = Error.Conflict("user.email_exists", "users.errors.email_exists");
    public static readonly Error PhoneAlreadyExists = Error.Conflict("user.phone_exists", "users.errors.phone_exists");
    public static readonly Error InvalidCredentials = Error.Validation("user.invalid_credentials", "users.errors.invalid_credentials");
    public static readonly Error EmailNotVerified = Error.Validation("user.email_not_verified", "users.errors.email_not_verified");
    public static readonly Error InvalidOrExpiredCode = Error.Validation("user.invalid_or_expired_code", "users.errors.invalid_or_expired_code");
    public static readonly Error InvalidOrExpiredResetToken = Error.Validation("user.invalid_or_expired_reset_token", "users.errors.invalid_or_expired_reset_token");
    public static readonly Error Inactive = Error.Validation("user.inactive", "users.errors.inactive");
}
