using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Impersonation.Start;
using ZonarHub.Application.Features.Impersonation.Stop;
using ZonarHub.Application.Features.Impersonation.Health;
using ZonarHub.Domain.Users;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.Impersonation;

/// <summary>
/// TDD tasks 2.2.1, 2.2.2, 2.2.3 — RED: handler-level integration tests.
/// These compile and FAIL until 2.3.x adds the handler implementations.
/// Satisfies: design §4.1, §8.1; REQ-IMP-001, REQ-IMP-007–009, REQ-IMP-013, REQ-IMP-020.
/// </summary>
public sealed class ImpersonationHandlerTests
{
    private static readonly DateTime Now = new(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc);

    // ─── POST /api/admin/impersonation/start — happy path ────────────────────

    [Fact]
    public async Task Start_HappyPath_ReturnTokenAndSession()
    {
        // Arrange
        var h = new ImpersonationTestHarness(Now, featureEnabled: true);
        var tenantId = Guid.NewGuid();
        var sysadmin = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId, "sysadmin@test.dev", "Sys Admin");
        var target = await h.SeedUserAsync(UserRole.Player, tenantId, "player@test.dev", "Player One");
        h.CurrentUser.Authenticate(sysadmin.Id.Value, sysadmin.Email);

        // Act
        var result = await h.Start.Handle(
            new StartImpersonationCommand(target.Id.Value, "Debugging issue"),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEqual(string.Empty, result.Value.Token);
        Assert.NotEqual(Guid.Empty, result.Value.SessionId);
        Assert.Equal(target.Id.Value, result.Value.Target.Id);
        Assert.True(result.Value.ExpiresAt > new DateTimeOffset(h.Clock.UtcNow));

        // Session row must exist in the store
        var session = h.SessionStore.FindSession(result.Value.SessionId);
        Assert.NotNull(session);
        Assert.Equal(sysadmin.Id.Value, session!.RealUserId);
        Assert.Equal(target.Id.Value, session.TargetUserId);

        // Audit row of type session_started must have been written
        Assert.Contains(h.SessionStore.AuditRows,
            r => r.EventType == "session_started" && r.SessionId == result.Value.SessionId);
    }

    // ─── POST /api/admin/impersonation/start — feature flag off ──────────────

    [Fact]
    public async Task Start_WhenFeatureFlagOff_ReturnsFeatureDisabledError()
    {
        // Arrange
        var h = new ImpersonationTestHarness(Now, featureEnabled: false);
        var tenantId = Guid.NewGuid();
        var sysadmin = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId, "sysadmin@test.dev", "Sys Admin");
        var target = await h.SeedUserAsync(UserRole.Player, tenantId, "player@test.dev", "Player One");
        h.CurrentUser.Authenticate(sysadmin.Id.Value, sysadmin.Email);

        // Act
        var result = await h.Start.Handle(
            new StartImpersonationCommand(target.Id.Value, null),
            CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("impersonation.featureDisabled", result.Error.Code);
    }

    // ─── POST /api/admin/impersonation/start — caller not sysadmin ───────────

    [Fact]
    public async Task Start_WhenCallerIsNotSysadmin_ReturnsForbiddenError()
    {
        // Arrange
        var h = new ImpersonationTestHarness(Now, featureEnabled: true);
        var tenantId = Guid.NewGuid();
        var admin = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@test.dev", "Admin");
        var target = await h.SeedUserAsync(UserRole.Player, tenantId, "player@test.dev", "Player One");
        h.CurrentUser.Authenticate(admin.Id.Value, admin.Email);

        // Act
        var result = await h.Start.Handle(
            new StartImpersonationCommand(target.Id.Value, null),
            CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("impersonation.notSysadmin", result.Error.Code);
    }

    // ─── POST /api/admin/impersonation/start — admin-on-admin rejected ────────

    [Fact]
    public async Task Start_WhenTargetIsSysadmin_ReturnsTargetInvalidError()
    {
        // Arrange
        var h = new ImpersonationTestHarness(Now, featureEnabled: true);
        var tenantId = Guid.NewGuid();
        var sysadmin = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId, "sysadmin@test.dev", "Sys Admin");
        var targetSysadmin = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId, "other-sysadmin@test.dev", "Other Admin");
        h.CurrentUser.Authenticate(sysadmin.Id.Value, sysadmin.Email);

        // Act
        var result = await h.Start.Handle(
            new StartImpersonationCommand(targetSysadmin.Id.Value, null),
            CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("impersonation.targetInvalid", result.Error.Code);
    }

    // ─── POST /api/admin/impersonation/start — cross-tenant rejected ──────────

    [Fact]
    public async Task Start_WhenTargetIsInDifferentTenant_ReturnsTargetInvalidError()
    {
        // Arrange
        var h = new ImpersonationTestHarness(Now, featureEnabled: true);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var sysadmin = await h.SeedUserAsync(UserRole.SystemAdmin, tenantA, "sysadmin@test.dev", "Sys Admin");
        var targetOtherTenant = await h.SeedUserAsync(UserRole.Player, tenantB, "player@other.dev", "Player B");
        h.CurrentUser.Authenticate(sysadmin.Id.Value, sysadmin.Email);

        // Act
        var result = await h.Start.Handle(
            new StartImpersonationCommand(targetOtherTenant.Id.Value, null),
            CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("impersonation.targetInvalid", result.Error.Code);
    }

    // ─── POST /api/admin/impersonation/start — already impersonating ──────────

    [Fact]
    public async Task Start_WhenAlreadyImpersonating_AutoRevokesOldSessionAndCreatesNew()
    {
        // Arrange
        var h = new ImpersonationTestHarness(Now, featureEnabled: true);
        var tenantId = Guid.NewGuid();
        var sysadmin = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId, "sysadmin@test.dev", "Sys Admin");
        var targetA = await h.SeedUserAsync(UserRole.Player, tenantId, "player-a@test.dev", "Player A");
        var targetB = await h.SeedUserAsync(UserRole.Player, tenantId, "player-b@test.dev", "Player B");
        h.CurrentUser.Authenticate(sysadmin.Id.Value, sysadmin.Email);

        // Act: start first session
        var firstResult = await h.Start.Handle(
            new StartImpersonationCommand(targetA.Id.Value, null),
            CancellationToken.None);
        Assert.True(firstResult.IsSuccess);
        var firstSessionId = firstResult.Value.SessionId;

        // Act: start second session while first is active
        var secondResult = await h.Start.Handle(
            new StartImpersonationCommand(targetB.Id.Value, null),
            CancellationToken.None);

        // Assert: second session created successfully
        Assert.True(secondResult.IsSuccess);
        Assert.NotEqual(firstSessionId, secondResult.Value.SessionId);

        // Assert: first session was auto-revoked
        var oldSession = h.SessionStore.FindSession(firstSessionId);
        Assert.NotNull(oldSession);
        Assert.NotNull(oldSession!.RevokedAt);

        // Assert: a session_stopped audit row for the old session was written
        Assert.Contains(h.SessionStore.AuditRows,
            r => r.EventType == "session_stopped" && r.SessionId == firstSessionId);

        // Assert: new session_started audit row for the new session
        Assert.Contains(h.SessionStore.AuditRows,
            r => r.EventType == "session_started" && r.SessionId == secondResult.Value.SessionId);
    }

    // ─── POST /api/admin/impersonation/stop — happy path ─────────────────────

    [Fact]
    public async Task Stop_HappyPath_RevokesSessionAndWritesAudit()
    {
        // Arrange
        var h = new ImpersonationTestHarness(Now, featureEnabled: true);
        var tenantId = Guid.NewGuid();
        var sysadmin = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId, "sysadmin@test.dev", "Sys Admin");
        var target = await h.SeedUserAsync(UserRole.Player, tenantId, "player@test.dev", "Player One");
        h.CurrentUser.Authenticate(sysadmin.Id.Value, sysadmin.Email);

        var startResult = await h.Start.Handle(
            new StartImpersonationCommand(target.Id.Value, null),
            CancellationToken.None);
        Assert.True(startResult.IsSuccess);
        var sessionId = startResult.Value.SessionId;

        // Switch context to impersonation (caller now carries imp_session_id)
        h.CurrentUser.SetImpersonation(sessionId, target.Id.Value);

        // Act
        var stopResult = await h.Stop.Handle(
            new StopImpersonationCommand(sessionId),
            CancellationToken.None);

        // Assert
        Assert.True(stopResult.IsSuccess);

        var session = h.SessionStore.FindSession(sessionId);
        Assert.NotNull(session!.RevokedAt);

        Assert.Contains(h.SessionStore.AuditRows,
            r => r.EventType == "session_stopped" && r.SessionId == sessionId);
    }

    // ─── POST /api/admin/impersonation/stop — no impersonation token ──────────

    [Fact]
    public async Task Stop_WhenCallerIsNotImpersonating_ReturnsNotImpersonatingError()
    {
        // Arrange
        var h = new ImpersonationTestHarness(Now, featureEnabled: true);
        var tenantId = Guid.NewGuid();
        var sysadmin = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId, "sysadmin@test.dev", "Sys Admin");
        h.CurrentUser.Authenticate(sysadmin.Id.Value, sysadmin.Email);
        // NOTE: CurrentUser.ImpersonationSessionId is NOT set — caller is not impersonating

        // Act
        var stopResult = await h.Stop.Handle(
            new StopImpersonationCommand(Guid.Empty),
            CancellationToken.None);

        // Assert
        Assert.False(stopResult.IsSuccess);
        Assert.Equal("impersonation.notImpersonating", stopResult.Error.Code);
    }

    // ─── GET /api/admin/impersonation/health ─────────────────────────────────

    [Fact]
    public async Task Health_WhenFeatureEnabled_ReturnsEnabledTrue()
    {
        // Arrange
        var h = new ImpersonationTestHarness(Now, featureEnabled: true);

        // Act
        var result = await h.Health.Handle(new GetImpersonationHealthQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Enabled);
    }

    [Fact]
    public async Task Health_WhenFeatureDisabled_ReturnsEnabledFalse()
    {
        // Arrange
        var h = new ImpersonationTestHarness(Now, featureEnabled: false);

        // Act
        var result = await h.Health.Handle(new GetImpersonationHealthQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Enabled);
    }
}

// ─── Test harness ──────────────────────────────────────────────────────────

internal sealed class ImpersonationTestHarness
{
    private static readonly DateTime DefaultNow = new(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc);

    public ImpersonationTestHarness(DateTime nowUtc, bool featureEnabled)
    {
        Clock = new TestClock(nowUtc);
        CurrentUser = new ImpersonationTestCurrentUser();

        var userStore = new InMemoryUserStore();
        Users = new InMemoryUserRepository(userStore);

        SessionStore = new InMemoryImpersonationSessionStore(() => new DateTimeOffset(Clock.UtcNow));

        var featureFlags = new StubImpersonationFeatureFlags(featureEnabled);
        var jwt = new StubImpersonationJwtTokenService(nowUtc.AddMinutes(30));

        Start = new StartImpersonationHandler(CurrentUser, Users, SessionStore, jwt, featureFlags, Clock);
        Stop = new StopImpersonationHandler(CurrentUser, SessionStore, Clock);
        Health = new GetImpersonationHealthHandler(featureFlags);
    }

    public TestClock Clock { get; }
    public ImpersonationTestCurrentUser CurrentUser { get; }
    public InMemoryUserRepository Users { get; }
    public InMemoryImpersonationSessionStore SessionStore { get; }

    public StartImpersonationHandler Start { get; }
    public StopImpersonationHandler Stop { get; }
    public GetImpersonationHealthHandler Health { get; }

    public async Task<User> SeedUserAsync(UserRole role, Guid? tenantId, string email, string fullName)
    {
        var create = User.Register(
            UserId.New(),
            email,
            fullName,
            phone: null,
            birthDate: null,
            passwordHash: "seed-hash",
            role,
            tenantId,
            Clock.UtcNow);

        if (create.IsFailure)
            throw new InvalidOperationException(create.Error.Code);

        await Users.AddAsync(create.Value, CancellationToken.None);
        return create.Value;
    }
}

/// <summary>
/// Extended test current-user that exposes impersonation session context.
/// </summary>
internal sealed class ImpersonationTestCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Email { get; private set; }
    public Guid? OrganizationId { get; private set; }

    // Extra: impersonation context fields
    public Guid? ImpersonationSessionId { get; private set; }
    public Guid? EffectiveUserId { get; private set; }

    public void Authenticate(Guid userId, string email, Guid? organizationId = null)
    {
        IsAuthenticated = true;
        UserId = userId;
        Email = email;
        OrganizationId = organizationId;
        ImpersonationSessionId = null;
        EffectiveUserId = null;
    }

    public void SetImpersonation(Guid sessionId, Guid effectiveUserId)
    {
        ImpersonationSessionId = sessionId;
        EffectiveUserId = effectiveUserId;
    }

    public void SignOut()
    {
        IsAuthenticated = false;
        UserId = null;
        Email = null;
        OrganizationId = null;
        ImpersonationSessionId = null;
        EffectiveUserId = null;
    }
}

/// <summary>
/// Stub feature-flag implementation for impersonation tests.
/// </summary>
internal sealed class StubImpersonationFeatureFlags : IImpersonationFeatureFlags
{
    public StubImpersonationFeatureFlags(bool enabled) => IsEnabled = enabled;
    public bool IsEnabled { get; }
}

/// <summary>
/// Stub JWT service for impersonation tests — returns a deterministic token.
/// </summary>
internal sealed class StubImpersonationJwtTokenService : IJwtTokenService
{
    private readonly DateTime _expiresAtUtc;

    public StubImpersonationJwtTokenService(DateTime expiresAtUtc)
    {
        _expiresAtUtc = expiresAtUtc;
    }

    public string GenerateAccessToken(User user) => $"access-{user.Id.Value}";

    public string GenerateRefreshToken() => "refresh-token";

    public DateTime AccessTokenExpiresAt() => _expiresAtUtc;

    public string GenerateImpersonationToken(
        User target,
        User realUser,
        Guid sessionId,
        DateTimeOffset expiresAt) =>
        $"imp-{sessionId}";
}
