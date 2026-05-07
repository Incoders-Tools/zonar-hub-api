using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Auth.CheckCode;
using ZonarHub.Application.Features.Auth.Register;
using ZonarHub.Domain.Tenants;
using ZonarHub.Domain.Users;
using ZonarHub.Infrastructure.Auth;
using ZonarHub.Infrastructure.Caching;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.Auth;

public sealed class AuthRegistrationTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Register_WhenOrphanedTenantExists_ReusesItAndSucceeds()
    {
        // Arrange: simulate a previous failed registration that left an orphaned tenant
        var h = new AuthTestHarness(Now);
        var orphanedTenantResult = Tenant.Create(
            TenantId.New(), "Nicolas Morales", string.Empty, "nm@gmail.com", TenantPlanType.Starter, Now);
        Assert.True(orphanedTenantResult.IsSuccess);
        await h.Tenants.AddAsync(orphanedTenantResult.Value, CancellationToken.None);

        // Act: register with the same email (no user exists, but tenant does)
        var result = await h.Register.Handle(
            new RegisterCommand(
                "Nicolas Morales",
                "nm@gmail.com",
                "Password123!",
                null,
                null,
                "451499"),
            CancellationToken.None);

        // Assert: registration succeeds by reusing the orphaned tenant
        Assert.True(result.IsSuccess);
        Assert.Equal("nm@gmail.com", result.Value.User.Email);

        var persisted = await h.Users.GetByEmailAsync("nm@gmail.com", CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.True(persisted!.IsEmailVerified);

        // The orphaned tenant's ID should be reused
        Assert.Equal(orphanedTenantResult.Value.Id.Value.ToString(), result.Value.Tenant?.Id);
    }

    [Fact]
    public async Task Register_WithMasterBypassCode_AllowsRegistrationWithoutCachedCode()
    {
        var h = new AuthTestHarness(Now);

        var result = await h.Register.Handle(
            new RegisterCommand(
                "Nicolas Morales",
                "nm@gmail.com",
                "Password123!",
                "+54 9 (11) 5555-1234",
                null,
                "451499"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("nm@gmail.com", result.Value.User.Email);
        Assert.Null(result.Value.User.TenantIds);
        Assert.Null(result.Value.User.OrganizationId);

        var persisted = await h.Users.GetByEmailAsync("nm@gmail.com", CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.True(persisted!.IsEmailVerified);
    }

    [Fact]
    public async Task Register_WithDuplicatedNormalizedPhone_ReturnsConflict()
    {
        var h = new AuthTestHarness(Now);

        var first = await h.Register.Handle(
            new RegisterCommand(
                "First User",
                "first@gmail.com",
                "Password123!",
                "+54 9 11 5555-1234",
                null,
                "451499"),
            CancellationToken.None);

        Assert.True(first.IsSuccess);

        var second = await h.Register.Handle(
            new RegisterCommand(
                "Second User",
                "second@gmail.com",
                "Password123!",
                "+5491155551234",
                null,
                "451499"),
            CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal("user.phone_exists", second.Error.Code);
    }

    [Fact]
    public async Task CheckCode_MasterBypass_IsAlwaysValid()
    {
        var cache = new MemoryCacheStore(TimeProvider.System);
        var handler = new CheckVerificationCodeHandler(cache);

        var result = await handler.Handle(
            new CheckVerificationCodeQuery("x@y.com", "451499"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task CheckCode_WithStoredCode_ValidatesAgainstCache()
    {
        var cache = new MemoryCacheStore(TimeProvider.System);
        const string email = "cache@test.com";
        await cache.SetAsync($"verify:{email}", "123456", TimeSpan.FromMinutes(15));

        var handler = new CheckVerificationCodeHandler(cache);

        var valid = await handler.Handle(new CheckVerificationCodeQuery(email, "123456"), CancellationToken.None);
        var invalid = await handler.Handle(new CheckVerificationCodeQuery(email, "000000"), CancellationToken.None);

        Assert.True(valid.IsSuccess);
        Assert.True(valid.Value);
        Assert.True(invalid.IsSuccess);
        Assert.False(invalid.Value);
    }

    private sealed class AuthTestHarness
    {
        public AuthTestHarness(DateTime nowUtc)
        {
            Users = new InMemoryUserRepository(new InMemoryUserStore());
            Tenants = new InMemoryTenantRepository(new InMemoryTenantStore());
            var hasher = new PasswordHasher();
            var jwt = new StubJwtTokenService(nowUtc.AddMinutes(30));
            var cache = new MemoryCacheStore(TimeProvider.System);
            var email = new NoopEmailService();
            var templates = new StubEmailTemplateComposer();
            var uow = new InMemoryUnitOfWork();
            var clock = new TestClock(nowUtc);

            Register = new RegisterHandler(Users, Tenants, hasher, jwt, cache, email, templates, uow, clock);
        }

        public InMemoryUserRepository Users { get; }
        public InMemoryTenantRepository Tenants { get; }
        public RegisterHandler Register { get; }
    }

    private sealed class StubJwtTokenService : IJwtTokenService
    {
        private readonly DateTime _expiresAtUtc;

        public StubJwtTokenService(DateTime expiresAtUtc)
        {
            _expiresAtUtc = expiresAtUtc;
        }

        public string GenerateAccessToken(User user) => $"access-{user.Id.Value}";

        public string GenerateRefreshToken() => "refresh-token";

        public DateTime AccessTokenExpiresAt() => _expiresAtUtc;
    }

    private sealed class NoopEmailService : IEmailService
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubEmailTemplateComposer : IEmailTemplateComposer
    {
        public Task<EmailMessage> ComposeAsync(
            string to,
            string templateKey,
            string fallbackSubject,
            string fallbackHtmlBody,
            IReadOnlyDictionary<string, string>? variables = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EmailMessage(to, fallbackSubject, fallbackHtmlBody));
    }
}
