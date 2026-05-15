using System.Security.Claims;

namespace ZonarHub.Infrastructure.Auth.Impersonation;

/// <summary>
/// POCO extracted from JWT claims during request processing.
/// Satisfies design §1, §2.3; REQ-AUD-004, REQ-AUD-005.
///
/// Populated once per request from the authenticated <see cref="ClaimsPrincipal"/>.
/// The middleware that populates this should reject (401) any request where
/// <see cref="IsImpersonating"/> is <c>true</c> but <see cref="IsValid"/> is <c>false</c>.
/// </summary>
public sealed class ImpersonationContext
{
    private ImpersonationContext() { }

    /// <summary>
    /// Whether the current token is an impersonation token.
    /// Determined by the presence and truth of the <c>imp</c> claim.
    /// </summary>
    public bool IsImpersonating { get; private init; }

    /// <summary>
    /// Whether the context is fully valid.
    /// For non-impersonation tokens, always <c>true</c>.
    /// For impersonation tokens, requires both <c>act.sub</c> and <c>imp_session_id</c> claims.
    /// </summary>
    public bool IsValid { get; private init; }

    /// <summary>
    /// The real (sysadmin) user's id, extracted from <c>act.sub</c>.
    /// <c>null</c> when <see cref="IsImpersonating"/> is <c>false</c>.
    /// </summary>
    public Guid? RealUserId { get; private init; }

    /// <summary>
    /// The server-side session record id, extracted from <c>imp_session_id</c>.
    /// <c>null</c> when <see cref="IsImpersonating"/> is <c>false</c>.
    /// </summary>
    public Guid? SessionId { get; private init; }

    /// <summary>
    /// The effective (impersonated) user's id, extracted from <c>sub</c>.
    /// Always populated when <see cref="IsImpersonating"/> is <c>true</c> and the token is valid.
    /// </summary>
    public Guid? EffectiveUserId { get; private init; }

    /// <summary>
    /// Factory: builds an <see cref="ImpersonationContext"/> from the claims
    /// on the current <see cref="ClaimsPrincipal"/>.
    /// </summary>
    public static ImpersonationContext FromPrincipal(ClaimsPrincipal principal)
    {
        var impClaim = principal.FindFirstValue("imp");
        var isImpersonating =
            !string.IsNullOrEmpty(impClaim) &&
            string.Equals(impClaim, "true", StringComparison.OrdinalIgnoreCase);

        if (!isImpersonating)
        {
            return new ImpersonationContext
            {
                IsImpersonating = false,
                IsValid = true,
                RealUserId = null,
                SessionId = null,
            };
        }

        // Impersonation token — validate required claims (REQ-AUD-005).
        var actSubRaw = principal.FindFirstValue("act.sub");
        var sessionIdRaw = principal.FindFirstValue("imp_session_id");
        var subRaw = principal.FindFirstValue("sub");

        var actSubParsed = Guid.TryParse(actSubRaw, out var actSubGuid);
        var sessionIdParsed = Guid.TryParse(sessionIdRaw, out var sessionIdGuid);
        var subParsed = Guid.TryParse(subRaw, out var subGuid);

        var isValid = actSubParsed && sessionIdParsed && subParsed;

        return new ImpersonationContext
        {
            IsImpersonating = true,
            IsValid = isValid,
            RealUserId = actSubParsed ? actSubGuid : null,
            SessionId = sessionIdParsed ? sessionIdGuid : null,
            EffectiveUserId = subParsed ? subGuid : null,
        };
    }
}
