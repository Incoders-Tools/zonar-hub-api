namespace ZonarHub.Application.Features.Auth;

public static class AuthEmailTemplateKeys
{
    public const string VerificationCode = "auth.verification_code";
    public const string PasswordReset = "auth.password_reset";
    public const string Welcome = "auth.welcome";

    public static readonly IReadOnlyList<string> SeedKeys =
    [
        VerificationCode,
        PasswordReset,
        Welcome,
        "auth.invite_user",
        "auth.magic_link",
        "auth.change_email",
        "auth.reauthentication",
    ];
}
