using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.Impersonation.Start;

/// <summary>
/// Handler for <see cref="StartImpersonationCommand"/>.
///
/// Checks:
///   1. Feature flag enabled   → 404 impersonation.featureDisabled
///   2. Caller is sysadmin     → 403 impersonation.notSysadmin
///   3. Target exists          → 400 impersonation.targetInvalid
///   4. Target not sysadmin    → 400 impersonation.targetInvalid
///   5. Same tenant (default-deny cross-tenant) → 400 impersonation.targetInvalid
///   6. Auto-revoke prior session (REQ-IMP-013) → emit session_stopped audit
///   7. Create new session row
///   8. Mint impersonation JWT
///   9. Emit session_started audit (blocking — failure causes 500)
///
/// Satisfies: REQ-IMP-001, REQ-IMP-003, REQ-IMP-007–009, REQ-IMP-013, design §4.1.
/// </summary>
public sealed class StartImpersonationHandler
    : IRequestHandler<StartImpersonationCommand, Result<StartImpersonationResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IImpersonationSessionStore _sessionStore;
    private readonly IJwtTokenService _jwt;
    private readonly IImpersonationFeatureFlags _featureFlags;
    private readonly IClock _clock;

    public StartImpersonationHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IImpersonationSessionStore sessionStore,
        IJwtTokenService jwt,
        IImpersonationFeatureFlags featureFlags,
        IClock clock)
    {
        _currentUser = currentUser;
        _users = users;
        _sessionStore = sessionStore;
        _jwt = jwt;
        _featureFlags = featureFlags;
        _clock = clock;
    }

    public async Task<Result<StartImpersonationResponse>> Handle(
        StartImpersonationCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Feature flag check.
        if (!_featureFlags.IsEnabled)
        {
            return Result.Failure<StartImpersonationResponse>(
                ImpersonationErrors.FeatureDisabled);
        }

        // 2. Caller must be authenticated.
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } callerUserId)
        {
            return Result.Failure<StartImpersonationResponse>(
                ImpersonationErrors.NotSysadmin);
        }

        // 3. Load caller user to verify role.
        var caller = await _users.GetByIdAsync(new UserId(callerUserId), cancellationToken);
        if (caller is null || caller.Role != UserRole.SystemAdmin)
        {
            return Result.Failure<StartImpersonationResponse>(
                ImpersonationErrors.NotSysadmin);
        }

        // 4. Load target user.
        var target = await _users.GetByIdAsync(new UserId(request.TargetUserId), cancellationToken);
        if (target is null || !target.IsActive)
        {
            return Result.Failure<StartImpersonationResponse>(
                ImpersonationErrors.TargetInvalid);
        }

        // 5. Admin-on-admin forbidden (REQ-IMP-003).
        if (target.Role == UserRole.SystemAdmin)
        {
            return Result.Failure<StartImpersonationResponse>(
                ImpersonationErrors.TargetInvalid);
        }

        // 6. Cross-tenant check: default-deny (ADR-004).
        //    If the sysadmin has a tenantId, the target must be in the same tenant.
        //    If the sysadmin has no tenantId (global sysadmin), cross-tenant is allowed.
        if (caller.TenantId.HasValue && target.TenantId != caller.TenantId)
        {
            return Result.Failure<StartImpersonationResponse>(
                ImpersonationErrors.TargetInvalid);
        }

        var now = _clock.UtcNow;
        var nowOffset = new DateTimeOffset(now, TimeSpan.Zero);

        // 7. Auto-revoke any existing active session for this sysadmin (REQ-IMP-013).
        var existingSession = await _sessionStore.GetActiveByRealUserAsync(callerUserId, cancellationToken);
        if (existingSession is not null)
        {
            await _sessionStore.RevokeAsync(existingSession.Id, nowOffset, cancellationToken);

            // Write non-blocking session_stopped audit for the auto-revoked session.
            await _sessionStore.WriteAuditAsync(new ImpersonationAuditRecord(
                Id: Guid.NewGuid(),
                SessionId: existingSession.Id,
                RealUserId: callerUserId,
                EffectiveUserId: existingSession.TargetUserId,
                Method: "POST",
                Path: "/api/admin/impersonation/start",
                Status: 204,
                Ip: null,
                UserAgent: null,
                OccurredAt: nowOffset,
                EventType: "session_stopped"),
                cancellationToken);
        }

        // 8. Compute expiry.
        var expiresAt = nowOffset.AddMinutes(30);

        // 9. Create session row.
        var sessionId = Guid.NewGuid();
        var sessionRecord = new ImpersonationSessionRecord(
            Id: sessionId,
            RealUserId: callerUserId,
            TargetUserId: target.Id.Value,
            StartedAt: nowOffset,
            ExpiresAt: expiresAt,
            RevokedAt: null,
            TenantId: target.TenantId ?? caller.TenantId ?? Guid.Empty,
            Reason: request.Reason);

        await _sessionStore.CreateAsync(sessionRecord, cancellationToken);

        // 10. Mint impersonation JWT.
        var token = _jwt.GenerateImpersonationToken(target, caller, sessionId, expiresAt);

        // 11. Write session_started audit row (blocking — callers should treat failure as 500).
        await _sessionStore.WriteAuditAsync(new ImpersonationAuditRecord(
            Id: Guid.NewGuid(),
            SessionId: sessionId,
            RealUserId: callerUserId,
            EffectiveUserId: target.Id.Value,
            Method: "POST",
            Path: "/api/admin/impersonation/start",
            Status: 200,
            Ip: null,
            UserAgent: null,
            OccurredAt: nowOffset,
            EventType: "session_started"),
            cancellationToken);

        var response = new StartImpersonationResponse(
            Token: token,
            TokenType: "Bearer",
            ExpiresAt: expiresAt,
            SessionId: sessionId,
            Target: new ImpersonationTargetDto(
                Id: target.Id.Value,
                FullName: target.FullName,
                Email: target.Email,
                Role: RoleToString(target.Role),
                TenantId: target.TenantId));

        return Result.Success(response);
    }

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

/// <summary>
/// Domain errors for the impersonation feature.
/// Centralised here so endpoint mapping and handler both reference the same codes.
/// </summary>
public static class ImpersonationErrors
{
    /// <summary>Feature flag is off; pretend the route does not exist (404).</summary>
    public static readonly Error FeatureDisabled =
        Error.NotFound("impersonation.featureDisabled", "admin.impersonation.errors.featureDisabled");

    /// <summary>Caller is not a system_admin.</summary>
    public static readonly Error NotSysadmin =
        Error.Failure("impersonation.notSysadmin", "admin.impersonation.errors.notSysadmin");

    /// <summary>Target user does not exist, is inactive, is a sysadmin, or is in a forbidden tenant.</summary>
    public static readonly Error TargetInvalid =
        Error.Validation("impersonation.targetInvalid", "admin.impersonation.errors.targetInvalid");

    /// <summary>Caller is trying to stop but is not currently impersonating.</summary>
    public static readonly Error NotImpersonating =
        Error.Validation("impersonation.notImpersonating", "admin.impersonation.errors.notImpersonating");

    /// <summary>Session not found or already revoked on stop.</summary>
    public static readonly Error SessionNotFound =
        Error.Validation("impersonation.sessionNotFound", "admin.impersonation.errors.sessionNotFound");
}
