using System.Security.Claims;
using ZonarHub.Infrastructure.Auth.Impersonation;

namespace ZonarHub.Tests.Infrastructure.Auth.Impersonation;

/// <summary>
/// Unit tests for <see cref="ImpersonationContext"/>.
/// Task 1.4.1 — RED until ImpersonationContext is implemented.
/// Verifies design §1 / §2.3: parsing claims, IsImpersonating flag, and
/// validation of malformed tokens.
/// </summary>
public sealed class ImpersonationContextTests
{
    private static readonly Guid RealUserId = Guid.NewGuid();
    private static readonly Guid EffectiveUserId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();

    // ── helpers ──────────────────────────────────────────────────────────

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal BuildImpersonationPrincipal(
        Guid? effectiveUserId = null,
        Guid? realUserId = null,
        Guid? sessionId = null)
    {
        var claims = new List<Claim>
        {
            new("sub", (effectiveUserId ?? EffectiveUserId).ToString()),
            new("imp", "true"),
            new("act.sub", (realUserId ?? RealUserId).ToString()),
            new("imp_session_id", (sessionId ?? SessionId).ToString()),
        };
        return BuildPrincipal(claims.ToArray());
    }

    // ── IsImpersonating ───────────────────────────────────────────────────

    [Fact]
    public void IsImpersonating_WhenImpClaimIsTrue_ReturnsTrue()
    {
        var principal = BuildImpersonationPrincipal();
        var context = ImpersonationContext.FromPrincipal(principal);

        Assert.True(context.IsImpersonating);
    }

    [Fact]
    public void IsImpersonating_WhenImpClaimAbsent_ReturnsFalse()
    {
        var principal = BuildPrincipal(
            new Claim("sub", EffectiveUserId.ToString()));

        var context = ImpersonationContext.FromPrincipal(principal);

        Assert.False(context.IsImpersonating);
    }

    [Fact]
    public void IsImpersonating_WhenImpClaimIsFalse_ReturnsFalse()
    {
        var principal = BuildPrincipal(
            new Claim("sub", EffectiveUserId.ToString()),
            new Claim("imp", "false"));

        var context = ImpersonationContext.FromPrincipal(principal);

        Assert.False(context.IsImpersonating);
    }

    // ── RealUserId ────────────────────────────────────────────────────────

    [Fact]
    public void RealUserId_WhenImpersonating_ParsesActSubClaim()
    {
        var principal = BuildImpersonationPrincipal(realUserId: RealUserId);
        var context = ImpersonationContext.FromPrincipal(principal);

        Assert.Equal(RealUserId, context.RealUserId);
    }

    [Fact]
    public void RealUserId_WhenNotImpersonating_IsNull()
    {
        var principal = BuildPrincipal(
            new Claim("sub", EffectiveUserId.ToString()));

        var context = ImpersonationContext.FromPrincipal(principal);

        Assert.Null(context.RealUserId);
    }

    // ── SessionId ─────────────────────────────────────────────────────────

    [Fact]
    public void SessionId_WhenImpersonating_ParsesImpSessionIdClaim()
    {
        var principal = BuildImpersonationPrincipal(sessionId: SessionId);
        var context = ImpersonationContext.FromPrincipal(principal);

        Assert.Equal(SessionId, context.SessionId);
    }

    [Fact]
    public void SessionId_WhenNotImpersonating_IsNull()
    {
        var principal = BuildPrincipal(
            new Claim("sub", EffectiveUserId.ToString()));

        var context = ImpersonationContext.FromPrincipal(principal);

        Assert.Null(context.SessionId);
    }

    // ── malformed token validation ────────────────────────────────────────

    [Fact]
    public void FromPrincipal_WhenImpIsTrueButActSubAbsent_IsInvalid()
    {
        // imp=true but no act.sub — malformed per design §2.3 / REQ-AUD-005
        var principal = BuildPrincipal(
            new Claim("sub", EffectiveUserId.ToString()),
            new Claim("imp", "true"),
            new Claim("imp_session_id", SessionId.ToString()));
        // act.sub intentionally missing

        var context = ImpersonationContext.FromPrincipal(principal);

        // Malformed token: imp=true but act.sub is missing → IsValid = false
        Assert.False(context.IsValid);
    }

    [Fact]
    public void FromPrincipal_WhenImpIsTrueButImpSessionIdAbsent_IsInvalid()
    {
        var principal = BuildPrincipal(
            new Claim("sub", EffectiveUserId.ToString()),
            new Claim("imp", "true"),
            new Claim("act.sub", RealUserId.ToString()));
        // imp_session_id intentionally missing

        var context = ImpersonationContext.FromPrincipal(principal);

        Assert.False(context.IsValid);
    }

    [Fact]
    public void FromPrincipal_WhenImpersonatingWithAllClaims_IsValid()
    {
        var principal = BuildImpersonationPrincipal();
        var context = ImpersonationContext.FromPrincipal(principal);

        Assert.True(context.IsValid);
    }

    [Fact]
    public void FromPrincipal_WhenNotImpersonating_IsValid()
    {
        // Non-impersonation tokens are always valid (no extra claims required)
        var principal = BuildPrincipal(
            new Claim("sub", EffectiveUserId.ToString()));

        var context = ImpersonationContext.FromPrincipal(principal);

        Assert.True(context.IsValid);
    }
}
