namespace ZonarHub.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Authentication:Jwt";

    public string Issuer { get; init; } = "ZonarHub";
    public string Audience { get; init; } = "ZonarHub";
    public string Secret { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 30;
    public int RefreshTokenDays { get; init; } = 7;
}
