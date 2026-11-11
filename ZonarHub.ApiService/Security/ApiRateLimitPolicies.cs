namespace ZonarHub.ApiService.Security;

internal static class ApiRateLimitPolicies
{
    public const string AnonymousAuth = "anonymous-auth";

    /// <summary>Requests of a public registration email code (each one sends an email), stricter than <see cref="AnonymousAuth"/>.</summary>
    public const string PublicRegistrationCode = "public-registration-code";
}
