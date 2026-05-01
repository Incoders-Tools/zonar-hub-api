using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Abstractions;

public interface IJwtTokenService
{
    string GenerateAccessToken(User user);

    string GenerateRefreshToken();

    DateTime AccessTokenExpiresAt();
}
