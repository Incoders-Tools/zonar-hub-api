using System.Security.Claims;
using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Impersonation.Health;
using ZonarHub.Domain.Common;
using ZonarHub.Infrastructure.Auth.Impersonation;
using ZonarHub.Infrastructure.Middleware;
using ZonarHub.Infrastructure.Persistence.InMemory;

namespace ZonarHub.Tests.Application.Impersonation;

/// <summary>
/// TDD task 2.5.1 — RED: tests for <see cref="ImpersonationAuditBehavior"/>.
/// Satisfies: REQ-AUD-001, REQ-AUD-002, REQ-AUD-003, REQ-AUD-004, REQ-AUD-010, design §4.3.
/// </summary>
public sealed class ImpersonationAuditBehaviorTests
{
    [Fact]
    public async Task Behavior_WhenImpersonating_WritesAuditRow()
    {
        // Arrange
        var store = new InMemoryImpersonationSessionStore();
        var realUserId = Guid.NewGuid();
        var effectiveUserId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var impContext = BuildImpersonatingContext(realUserId, effectiveUserId, sessionId);
        var behavior = new ImpersonationAuditBehavior<GetImpersonationHealthQuery, Result<ImpersonationHealthResponse>>(
            store,
            impContext,
            new StubHttpContextInfo("GET", "/api/admin/impersonation/health", "127.0.0.1", "TestAgent/1.0"));

        var query = new GetImpersonationHealthQuery();
        var handler = new SimpleHandler<GetImpersonationHealthQuery, Result<ImpersonationHealthResponse>>(
            _ => Task.FromResult(Result.Success(new ImpersonationHealthResponse(true))));

        // Act
        await behavior.Handle(query, handler.Handle, CancellationToken.None);

        // Assert: exactly one audit row written
        Assert.Single(store.AuditRows);
        var row = store.AuditRows[0];
        Assert.Equal(realUserId, row.RealUserId);
        Assert.Equal(effectiveUserId, row.EffectiveUserId);
        Assert.Equal(sessionId, row.SessionId);
        Assert.Equal("GET", row.Method);
        Assert.Equal("/api/admin/impersonation/health", row.Path);
        Assert.NotEqual(default, row.OccurredAt);
    }

    [Fact]
    public async Task Behavior_WhenNotImpersonating_WritesNoAuditRow()
    {
        // Arrange
        var store = new InMemoryImpersonationSessionStore();
        var impContext = BuildNonImpersonatingContext();
        var behavior = new ImpersonationAuditBehavior<GetImpersonationHealthQuery, Result<ImpersonationHealthResponse>>(
            store,
            impContext,
            new StubHttpContextInfo("GET", "/api/health", "127.0.0.1", "TestAgent/1.0"));

        var query = new GetImpersonationHealthQuery();
        var handler = new SimpleHandler<GetImpersonationHealthQuery, Result<ImpersonationHealthResponse>>(
            _ => Task.FromResult(Result.Success(new ImpersonationHealthResponse(false))));

        // Act
        await behavior.Handle(query, handler.Handle, CancellationToken.None);

        // Assert: no rows written
        Assert.Empty(store.AuditRows);
    }

    private static ImpersonationContext BuildImpersonatingContext(Guid realUserId, Guid effectiveUserId, Guid sessionId)
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("imp", "true"),
            new Claim("act.sub", realUserId.ToString()),
            new Claim("imp_session_id", sessionId.ToString()),
            new Claim("sub", effectiveUserId.ToString()),
        }, "test"));

        return ImpersonationContext.FromPrincipal(claims);
    }

    private static ImpersonationContext BuildNonImpersonatingContext()
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
        }, "test"));

        return ImpersonationContext.FromPrincipal(claims);
    }
}

// ─── Test helpers ──────────────────────────────────────────────────────────

internal sealed class SimpleHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly Func<TRequest, Task<TResponse>> _fn;

    public SimpleHandler(Func<TRequest, Task<TResponse>> fn)
    {
        _fn = fn;
    }

    public Task<TResponse> Handle(CancellationToken ct = default) => _fn(default!);
}

/// <summary>Stub that provides HTTP context info for audit rows in tests.</summary>
internal sealed class StubHttpContextInfo : IHttpContextInfo
{
    public StubHttpContextInfo(string method, string path, string ip, string userAgent)
    {
        Method = method;
        Path = path;
        Ip = ip;
        UserAgent = userAgent;
    }

    public string Method { get; }
    public string Path { get; }
    public string Ip { get; }
    public string UserAgent { get; }
}
