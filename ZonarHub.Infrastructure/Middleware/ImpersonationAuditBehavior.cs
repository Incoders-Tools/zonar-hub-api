using MediatR;
using Microsoft.Extensions.Logging;
using ZonarHub.Application.Abstractions;
using ZonarHub.Infrastructure.Auth.Impersonation;

namespace ZonarHub.Infrastructure.Middleware;

/// <summary>
/// MediatR pipeline behavior that writes an audit row to <c>impersonation_audit</c>
/// after every authenticated handler when <see cref="ImpersonationContext.IsImpersonating"/> is true.
///
/// Audit failure is non-blocking for runtime requests (REQ-AUD-009):
/// logs the exception and continues without re-throwing.
///
/// Satisfies: REQ-AUD-001, REQ-AUD-002, REQ-AUD-003, REQ-AUD-004, REQ-AUD-010, REQ-AUD-012, REQ-AUD-013, design §4.3.
/// </summary>
public sealed class ImpersonationAuditBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IImpersonationSessionStore _store;
    private readonly ImpersonationContext _context;
    private readonly IHttpContextInfo _httpInfo;
    private ILogger<ImpersonationAuditBehavior<TRequest, TResponse>>? _logger;

    /// <summary>
    /// Constructor used in tests (no logger required — failure swallowed silently).
    /// </summary>
    public ImpersonationAuditBehavior(
        IImpersonationSessionStore store,
        ImpersonationContext context,
        IHttpContextInfo httpInfo)
    {
        _store = store;
        _context = context;
        _httpInfo = httpInfo;
    }

    /// <summary>
    /// Constructor used in production (with logger for audit-failure telemetry).
    /// </summary>
    public ImpersonationAuditBehavior(
        IImpersonationSessionStore store,
        ImpersonationContext context,
        IHttpContextInfo httpInfo,
        ILogger<ImpersonationAuditBehavior<TRequest, TResponse>> logger)
        : this(store, context, httpInfo)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken);

        // Only audit when an impersonation token is in use.
        if (!_context.IsImpersonating || !_context.IsValid)
        {
            return response;
        }

        try
        {
            await _store.WriteAuditAsync(new ImpersonationAuditRecord(
                Id: Guid.NewGuid(),
                SessionId: _context.SessionId!.Value,
                RealUserId: _context.RealUserId!.Value,           // act.sub
                EffectiveUserId: _context.EffectiveUserId!.Value, // sub
                Method: _httpInfo.Method,
                Path: _httpInfo.Path,
                Status: null,   // Status not available inside the MediatR pipeline
                Ip: _httpInfo.Ip,
                UserAgent: _httpInfo.UserAgent,
                OccurredAt: DateTimeOffset.UtcNow,
                EventType: "request"),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Non-blocking: log and swallow (REQ-AUD-009).
            _logger?.LogError(ex,
                "ImpersonationAuditBehavior: failed to write audit row for session {SessionId}. " +
                "The request itself is NOT affected.",
                _context.SessionId);
        }

        return response;
    }
}
