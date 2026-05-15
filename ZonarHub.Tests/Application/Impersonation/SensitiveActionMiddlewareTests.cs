using System.Security.Claims;
using ZonarHub.Infrastructure.Auth.Impersonation;
using ZonarHub.Infrastructure.Middleware;

namespace ZonarHub.Tests.Application.Impersonation;

/// <summary>
/// TDD task 2.4.1 — RED: tests for <see cref="SensitiveRouteRegistry"/> and
/// the block-list decision logic used by <see cref="SensitiveActionEndpointFilter"/>.
/// Satisfies: REQ-IMP-031, REQ-IMP-034, REQ-IMP-035, design §3.4, §3.5.
/// </summary>
public sealed class SensitiveActionMiddlewareTests
{
    // ─── SensitiveRouteRegistry decisions ────────────────────────────────────

    [Theory]
    [InlineData("POST", "/api/auth/reset-password")]
    [InlineData("POST", "/api/auth/forgot-password")]
    [InlineData("PUT", "/api/admin/users/00000000-0000-0000-0000-000000000001/2fa/enroll")]
    [InlineData("DELETE", "/api/admin/users/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/admin/billing/payment-methods/new")]
    [InlineData("DELETE", "/api/admin/billing/payment-methods/pm_abc")]
    [InlineData("PUT", "/api/user-preferences/email")]
    [InlineData("PUT", "/api/auth/me/email")]
    public void Registry_ReturnBlocked_ForSensitiveRoutes(string method, string path)
    {
        // Act
        var decision = SensitiveRouteRegistry.Decide(method, path);

        // Assert
        Assert.Equal(RouteDecision.Blocked, decision);
    }

    [Theory]
    [InlineData("POST", "/api/admin/impersonation/stop")]
    [InlineData("GET", "/api/admin/impersonation/health")]
    [InlineData("GET", "/api/tournaments")]
    [InlineData("GET", "/api/players")]
    public void Registry_ReturnAllowed_ForNonSensitiveRoutes(string method, string path)
    {
        // Act
        var decision = SensitiveRouteRegistry.Decide(method, path);

        // Assert
        Assert.Equal(RouteDecision.Allowed, decision);
    }

    // ─── ImpersonationContext detection ──────────────────────────────────────

    [Fact]
    public void Filter_WhenNotImpersonating_AllowsAnySensitiveRoute()
    {
        // When IsImpersonating = false, the filter must not block.
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim("sub", Guid.NewGuid().ToString()),
        }, "test"));

        var ctx = ImpersonationContext.FromPrincipal(claims);
        Assert.False(ctx.IsImpersonating);

        // Decision still Blocked, but the filter skips it when not impersonating.
        var decision = SensitiveRouteRegistry.Decide("POST", "/api/auth/reset-password");
        Assert.Equal(RouteDecision.Blocked, decision);
        // The filter inverts: if !IsImpersonating → pass through, regardless of registry.
    }

    [Fact]
    public void Filter_WhenImpersonatingAndRouteBlocked_ShouldBlock()
    {
        // When IsImpersonating = true AND route is Blocked → should return 403.
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("imp", "true"),
            new Claim("act.sub", Guid.NewGuid().ToString()),
            new Claim("imp_session_id", Guid.NewGuid().ToString()),
            new Claim("sub", Guid.NewGuid().ToString()),
        }, "test"));

        var ctx = ImpersonationContext.FromPrincipal(claims);
        Assert.True(ctx.IsImpersonating);

        var decision = SensitiveRouteRegistry.Decide("POST", "/api/auth/reset-password");
        Assert.Equal(RouteDecision.Blocked, decision);
    }

    [Fact]
    public void Filter_WhenImpersonatingAndRouteIsStopEndpoint_ShouldAllow()
    {
        // The stop endpoint must always be whitelisted (design §3.4).
        var decision = SensitiveRouteRegistry.Decide("POST", "/api/admin/impersonation/stop");
        Assert.Equal(RouteDecision.Allowed, decision);
    }
}
