namespace ZonarHub.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Authentication:Jwt";

    public string Issuer { get; init; } = "ZonarHub";
    public string Audience { get; init; } = "ZonarHub";
    public string Secret { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 30;
    public int RefreshTokenDays { get; init; } = 7;

    /// <summary>
    /// Lifetime of impersonation tokens in minutes (absolute, non-extendable).
    /// Design §2.2: 30 minutes matches the existing access-token TTL and bounds blast radius.
    /// </summary>
    public int ImpersonationTokenMinutes { get; init; } = 30;
}
