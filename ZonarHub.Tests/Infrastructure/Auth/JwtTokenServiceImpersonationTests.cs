using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using ZonarHub.Domain.Users;
using ZonarHub.Infrastructure.Auth;

namespace ZonarHub.Tests.Infrastructure.Auth;

/// <summary>
/// Unit tests for <see cref="JwtTokenService.GenerateImpersonationToken"/>.
/// Task 1.3.1 — RED until GenerateImpersonationToken is implemented.
/// Verifies claim shape per design §2.1.
/// </summary>
public sealed class JwtTokenServiceImpersonationTests
{
    private const string Secret = "test-secret-that-is-long-enough-for-hmac-sha256";
    private const string Issuer = "ZonarHub";
    private const string Audience = "ZonarHub";

    private static readonly DateTime FixedNow = new(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedNowOffset = new(FixedNow, TimeSpan.Zero);

    // ── target / real users ──────────────────────────────────────────────

    private static User BuildTargetUser()
    {
        var userId = UserId.New();
        var tenantId = Guid.NewGuid();
        var result = User.Register(
            userId,
            "target@example.com",
            "Target User",
            phone: null,
            birthDate: null,
            passwordHash: "hash",
            role: UserRole.Player,
            tenantId: tenantId,
            nowUtc: FixedNow);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static User BuildRealUser()
    {
        var result = User.Register(
            UserId.New(),
            "admin@example.com",
            "Real Sysadmin",
            phone: null,
            birthDate: null,
            passwordHash: "hash",
            role: UserRole.SystemAdmin,
            tenantId: Guid.NewGuid(),
            nowUtc: FixedNow);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static JwtTokenService BuildService(int impersonationTokenMinutes = 30)
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = Issuer,
            Audience = Audience,
            Secret = Secret,
            AccessTokenMinutes = 30,
            ImpersonationTokenMinutes = impersonationTokenMinutes,
        });

        var timeProvider = new FakeTimeProvider(FixedNowOffset);
        return new JwtTokenService(options, timeProvider);
    }

    private static JwtSecurityToken ParseToken(string tokenString)
    {
        var handler = new JwtSecurityTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            // Lifetime validation is intentionally disabled here because the tests use a
            // fixed "future" DateTime (2026-05-15T10:00:00Z) that may be ahead of the real
            // system clock on the test machine. We are testing claim values, not expiry
            // enforcement — that is the responsibility of the JWT middleware.
            ValidateLifetime = false,
        };

        handler.ValidateToken(tokenString, validationParams, out var validatedToken);
        return (JwtSecurityToken)validatedToken;
    }

    // ── 1.3.1 tests ──────────────────────────────────────────────────────

    [Fact]
    public void GenerateImpersonationToken_Sub_IsTargetUserId()
    {
        var target = BuildTargetUser();
        var real = BuildRealUser();
        var sessionId = Guid.NewGuid();
        var expiresAt = FixedNowOffset.AddMinutes(30);

        var svc = BuildService();
        var token = svc.GenerateImpersonationToken(target, real, sessionId, expiresAt);

        var parsed = ParseToken(token);
        var sub = parsed.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value;
        Assert.Equal(target.Id.Value.ToString(), sub);
    }

    [Fact]
    public void GenerateImpersonationToken_Email_IsTargetEmail()
    {
        var target = BuildTargetUser();
        var real = BuildRealUser();
        var sessionId = Guid.NewGuid();
        var expiresAt = FixedNowOffset.AddMinutes(30);

        var svc = BuildService();
        var token = svc.GenerateImpersonationToken(target, real, sessionId, expiresAt);

        var parsed = ParseToken(token);
        var email = parsed.Claims.FirstOrDefault(c =>
            c.Type == JwtRegisteredClaimNames.Email || c.Type == "email")?.Value;
        Assert.Equal(target.Email, email);
    }

    [Fact]
    public void GenerateImpersonationToken_ActSub_IsRealUserId()
    {
        var target = BuildTargetUser();
        var real = BuildRealUser();
        var sessionId = Guid.NewGuid();
        var expiresAt = FixedNowOffset.AddMinutes(30);

        var svc = BuildService();
        var token = svc.GenerateImpersonationToken(target, real, sessionId, expiresAt);

        var parsed = ParseToken(token);
        var actSub = parsed.Claims.FirstOrDefault(c => c.Type == "act.sub")?.Value;
        Assert.Equal(real.Id.Value.ToString(), actSub);
    }

    [Fact]
    public void GenerateImpersonationToken_ActEmail_IsRealUserEmail()
    {
        var target = BuildTargetUser();
        var real = BuildRealUser();
        var sessionId = Guid.NewGuid();
        var expiresAt = FixedNowOffset.AddMinutes(30);

        var svc = BuildService();
        var token = svc.GenerateImpersonationToken(target, real, sessionId, expiresAt);

        var parsed = ParseToken(token);
        var actEmail = parsed.Claims.FirstOrDefault(c => c.Type == "act.email")?.Value;
        Assert.Equal(real.Email, actEmail);
    }

    [Fact]
    public void GenerateImpersonationToken_ImpSessionId_MatchesProvidedSessionId()
    {
        var target = BuildTargetUser();
        var real = BuildRealUser();
        var sessionId = Guid.NewGuid();
        var expiresAt = FixedNowOffset.AddMinutes(30);

        var svc = BuildService();
        var token = svc.GenerateImpersonationToken(target, real, sessionId, expiresAt);

        var parsed = ParseToken(token);
        var impSessionId = parsed.Claims.FirstOrDefault(c => c.Type == "imp_session_id")?.Value;
        Assert.Equal(sessionId.ToString(), impSessionId);
    }

    [Fact]
    public void GenerateImpersonationToken_Imp_IsTrue()
    {
        var target = BuildTargetUser();
        var real = BuildRealUser();
        var sessionId = Guid.NewGuid();
        var expiresAt = FixedNowOffset.AddMinutes(30);

        var svc = BuildService();
        var token = svc.GenerateImpersonationToken(target, real, sessionId, expiresAt);

        var parsed = ParseToken(token);
        var imp = parsed.Claims.FirstOrDefault(c => c.Type == "imp")?.Value;
        Assert.Equal("true", imp, ignoreCase: true);
    }

    [Fact]
    public void GenerateImpersonationToken_Jti_IsNonEmpty()
    {
        var target = BuildTargetUser();
        var real = BuildRealUser();
        var sessionId = Guid.NewGuid();
        var expiresAt = FixedNowOffset.AddMinutes(30);

        var svc = BuildService();
        var token = svc.GenerateImpersonationToken(target, real, sessionId, expiresAt);

        var parsed = ParseToken(token);
        var jti = parsed.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;
        Assert.False(string.IsNullOrWhiteSpace(jti));
    }

    [Fact]
    public void GenerateImpersonationToken_Expiry_MatchesProvidedExpiresAt()
    {
        var target = BuildTargetUser();
        var real = BuildRealUser();
        var sessionId = Guid.NewGuid();
        var expiresAt = FixedNowOffset.AddMinutes(30);

        var svc = BuildService();
        var token = svc.GenerateImpersonationToken(target, real, sessionId, expiresAt);

        var parsed = ParseToken(token);
        // JWT exp is in seconds; compare with 1-second tolerance.
        var expectedExp = new DateTimeOffset(expiresAt.UtcDateTime, TimeSpan.Zero).ToUnixTimeSeconds();
        var actualExp = new DateTimeOffset(parsed.ValidTo, TimeSpan.Zero).ToUnixTimeSeconds();
        Assert.Equal(expectedExp, actualExp, tolerance: 1);
    }

    [Fact]
    public void GenerateImpersonationToken_TenantId_IsTargetUserTenantId()
    {
        var target = BuildTargetUser();
        var real = BuildRealUser();
        var sessionId = Guid.NewGuid();
        var expiresAt = FixedNowOffset.AddMinutes(30);

        var svc = BuildService();
        var token = svc.GenerateImpersonationToken(target, real, sessionId, expiresAt);

        var parsed = ParseToken(token);
        var tenantId = parsed.Claims.FirstOrDefault(c => c.Type == "tenantId")?.Value;
        Assert.Equal(target.TenantId?.ToString() ?? string.Empty, tenantId);
    }

    [Fact]
    public void GenerateImpersonationToken_Role_IsTargetUserRole()
    {
        var target = BuildTargetUser(); // UserRole.Player
        var real = BuildRealUser();
        var sessionId = Guid.NewGuid();
        var expiresAt = FixedNowOffset.AddMinutes(30);

        var svc = BuildService();
        var token = svc.GenerateImpersonationToken(target, real, sessionId, expiresAt);

        var parsed = ParseToken(token);
        // Role can appear as ClaimTypes.Role or "role"
        var role = parsed.Claims.FirstOrDefault(c =>
            c.Type == ClaimTypes.Role ||
            c.Type == "role" ||
            c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;
        Assert.Equal("player", role);
    }

    // ── non-impersonation token MUST NOT carry act/imp claims ────────────

    [Fact]
    public void GenerateAccessToken_DoesNotContainActClaim()
    {
        var target = BuildTargetUser();
        var svc = BuildService();
        var token = svc.GenerateAccessToken(target);

        var parsed = ParseToken(token);
        Assert.DoesNotContain(parsed.Claims, c => c.Type == "act.sub");
        Assert.DoesNotContain(parsed.Claims, c => c.Type == "act.email");
    }

    [Fact]
    public void GenerateAccessToken_DoesNotContainImpSessionIdClaim()
    {
        var target = BuildTargetUser();
        var svc = BuildService();
        var token = svc.GenerateAccessToken(target);

        var parsed = ParseToken(token);
        Assert.DoesNotContain(parsed.Claims, c => c.Type == "imp_session_id");
    }

    [Fact]
    public void GenerateAccessToken_DoesNotContainImpClaim()
    {
        var target = BuildTargetUser();
        var svc = BuildService();
        var token = svc.GenerateAccessToken(target);

        var parsed = ParseToken(token);
        Assert.DoesNotContain(parsed.Claims, c => c.Type == "imp");
    }

    // ── FakeTimeProvider ─────────────────────────────────────────────────

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
