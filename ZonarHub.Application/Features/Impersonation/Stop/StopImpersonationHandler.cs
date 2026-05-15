using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Impersonation.Start;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.Impersonation.Stop;

/// <summary>
/// Handler for <see cref="StopImpersonationCommand"/>.
///
/// 1. Validates the session ID is present in the command (non-empty).
/// 2. Looks up the session record.
/// 3. Revokes it (sets revoked_at = now).
/// 4. Writes session_stopped audit row (non-blocking per REQ-AUD-009).
/// 5. Returns 204.
///
/// The endpoint maps an empty SessionId when the caller is not impersonating
/// (no <c>imp_session_id</c> claim in JWT) → triggers the notImpersonating error.
///
/// Satisfies: REQ-IMP-020, REQ-AUD-007, REQ-AUD-009, REQ-AUD-011, design §4.1.
/// </summary>
public sealed class StopImpersonationHandler : IRequestHandler<StopImpersonationCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IImpersonationSessionStore _sessionStore;
    private readonly IClock _clock;

    public StopImpersonationHandler(
        ICurrentUser currentUser,
        IImpersonationSessionStore sessionStore,
        IClock clock)
    {
        _currentUser = currentUser;
        _sessionStore = sessionStore;
        _clock = clock;
    }

    public async Task<Result> Handle(
        StopImpersonationCommand request,
        CancellationToken cancellationToken)
    {
        // When the session ID is empty the caller is not impersonating.
        if (request.SessionId == Guid.Empty)
        {
            return Result.Failure(ImpersonationErrors.NotImpersonating);
        }

        var now = new DateTimeOffset(_clock.UtcNow, TimeSpan.Zero);
        var callerId = _currentUser.UserId;

        // Revoke the session.
        await _sessionStore.RevokeAsync(request.SessionId, now, cancellationToken);

        // Write non-blocking session_stopped audit row (REQ-AUD-009).
        try
        {
            await _sessionStore.WriteAuditAsync(new ImpersonationAuditRecord(
                Id: Guid.NewGuid(),
                SessionId: request.SessionId,
                RealUserId: callerId ?? Guid.Empty,
                EffectiveUserId: _currentUser.UserId ?? Guid.Empty,
                Method: "POST",
                Path: "/api/admin/impersonation/stop",
                Status: 204,
                Ip: null,
                UserAgent: null,
                OccurredAt: now,
                EventType: "session_stopped"),
                cancellationToken);
        }
        catch
        {
            // Audit failure is non-blocking for runtime requests (REQ-AUD-009).
            // TODO: log and emit metric here when ILogger is wired.
        }

        return Result.Success();
    }
}
