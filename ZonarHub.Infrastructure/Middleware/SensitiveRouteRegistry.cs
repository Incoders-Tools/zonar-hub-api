namespace ZonarHub.Infrastructure.Middleware;

/// <summary>
/// Indicates how a route should be treated when called under an impersonation token.
/// Satisfies: design §3.4, §3.5.
/// </summary>
public enum RouteDecision
{
    /// <summary>Request is allowed to proceed.</summary>
    Allowed = 0,

    /// <summary>Request is blocked — returns 403 impersonation.forbidden.</summary>
    Blocked = 1,

    /// <summary>Only GET/HEAD methods are allowed; mutations are blocked.</summary>
    ReadOnly = 2,
}

/// <summary>
/// Static registry of sensitive routes that must be blocked (or read-only restricted)
/// when called under an impersonation token.
///
/// Design §3.4: adding/removing entries is a one-line change here.
/// NOT driven by config — this is a security boundary and intentionally hard-coded
/// so that dynamic config cannot inadvertently open a dangerous route.
///
/// Whitelisted routes (always Allowed, even under impersonation):
///   POST /api/admin/impersonation/stop — sysadmin must always be able to exit.
///   GET  /api/admin/impersonation/health — anonymous, no risk.
///
/// Satisfies: REQ-IMP-031, REQ-IMP-034, REQ-IMP-035, design §3.4, §3.5.
/// </summary>
public static class SensitiveRouteRegistry
{
    // Whitelist: routes that must always be accessible under impersonation.
    private static readonly HashSet<(string Method, string PathPrefix)> _whiteList =
    [
        ("POST", "/api/admin/impersonation/stop"),
        ("GET", "/api/admin/impersonation/health"),
        ("GET", "/api/admin/impersonation/"),
    ];

    // Blocklist: routes that must be fully blocked under impersonation.
    private static readonly (string Method, string PathPrefix)[] _blocked =
    [
        ("POST", "/api/auth/reset-password"),
        ("POST", "/api/auth/forgot-password"),
        ("PUT", "/api/admin/users/"),          // covers /users/{id}/2fa/*
        ("DELETE", "/api/admin/users/"),        // account deletion
        ("POST", "/api/admin/billing/payment-methods"),
        ("DELETE", "/api/admin/billing/payment-methods"),
        ("PUT", "/api/user-preferences/email"),
        ("PUT", "/api/auth/me/email"),
    ];

    // ReadOnly: routes where only GET/HEAD is permitted under impersonation.
    private static readonly (string Method, string PathPrefix)[] _readOnly = [];

    /// <summary>
    /// Determines how the given <paramref name="method"/> + <paramref name="path"/> should be
    /// treated when called under an impersonation token.
    /// </summary>
    public static RouteDecision Decide(string method, string path)
    {
        var m = method.ToUpperInvariant();

        // Whitelist check first — always pass through these routes.
        foreach (var (wm, wp) in _whiteList)
        {
            if (string.Equals(wm, m, StringComparison.OrdinalIgnoreCase) &&
                path.StartsWith(wp, StringComparison.OrdinalIgnoreCase))
            {
                return RouteDecision.Allowed;
            }
        }

        // Exact-match whitelist (path without method prefix, e.g. health GET).
        // Already covered above.

        // Blocklist check.
        foreach (var (bm, bp) in _blocked)
        {
            if (string.Equals(bm, m, StringComparison.OrdinalIgnoreCase) &&
                path.StartsWith(bp, StringComparison.OrdinalIgnoreCase))
            {
                return RouteDecision.Blocked;
            }
        }

        return RouteDecision.Allowed;
    }
}
