using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Abstractions;

public interface IJwtTokenService
{
    string GenerateAccessToken(User user);

    string GenerateRefreshToken();

    DateTime AccessTokenExpiresAt();

    /// <summary>
    /// Generates a short-lived impersonation JWT carrying both the effective user identity
    /// (<paramref name="target"/>) and the real sysadmin identity (<paramref name="realUser"/>)
    /// as defined in design §2.1. Does NOT touch <see cref="GenerateAccessToken"/>.
    /// </summary>
    string GenerateImpersonationToken(
        User target,
        User realUser,
        Guid sessionId,
        DateTimeOffset expiresAt);
}
