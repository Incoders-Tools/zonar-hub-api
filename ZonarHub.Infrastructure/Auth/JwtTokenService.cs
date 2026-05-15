using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Users;

namespace ZonarHub.Infrastructure.Auth;

internal sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public string GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.Value.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, RoleToString(user.Role)),
            new Claim("tenantId", user.TenantId?.ToString() ?? string.Empty),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateImpersonationToken(
        User target,
        User realUser,
        Guid sessionId,
        DateTimeOffset expiresAt)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Design §2.1: sub = effective user (target); act.sub/act.email = real sysadmin.
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, target.Id.Value.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, target.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, RoleToString(target.Role)),
            new Claim("tenantId", target.TenantId?.ToString() ?? string.Empty),

            // RFC 8693 "act" claim — real sysadmin identity.
            new Claim("act.sub", realUser.Id.Value.ToString()),
            new Claim("act.email", realUser.Email),

            // Sentinel claims for fast detection without decoding the whole token.
            new Claim("imp_session_id", sessionId.ToString()),
            new Claim("imp", "true"),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));

    public DateTime AccessTokenExpiresAt() =>
        _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(_options.AccessTokenMinutes);

    private static string RoleToString(UserRole role) => role switch
    {
        UserRole.SystemAdmin => "system_admin",
        UserRole.Admin => "admin",
        UserRole.Editor => "editor",
        UserRole.User => "user",
        UserRole.Player => "player",
        _ => "viewer",
    };
}
