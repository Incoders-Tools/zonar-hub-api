using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Auth.Login;
using ZonarHub.Domain.Tenants;
using ZonarHub.Domain.Users;
using ZonarHub.Infrastructure.Auth;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.Auth;

public sealed class LoginHandlerTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Login_WithAssignedOrganizations_ReturnsOrganizationIdsWithCurrentFirst()
    {
        var h = new LoginTestHarness(Now);
        var tenant = await CreateTenantAsync(h, "Tenant A", "tenant-a");
        var user = await CreateVerifiedAdminAsync(h, tenant.Id.Value, "admin@tenant-a.dev", "Password123!");

        var firstOrganizationId = Guid.NewGuid();
        var currentOrganizationId = Guid.NewGuid();

        var assignCurrent = user.AssignOrganization(currentOrganizationId, Now);
        Assert.True(assignCurrent.IsSuccess);
        h.Users.Update(user);

        await h.Assignments.SetOrganizationIdsAsync(
            user.Id.Value,
            [firstOrganizationId, currentOrganizationId],
            CancellationToken.None);

        var result = await h.Login.Handle(
            new LoginCommand("admin@tenant-a.dev", "Password123!"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(currentOrganizationId.ToString(), result.Value.User.OrganizationId);
        Assert.NotNull(result.Value.User.TenantIds);
        Assert.Equal(
            new[] { currentOrganizationId.ToString(), firstOrganizationId.ToString() },
            result.Value.User.TenantIds);
    }

    [Fact]
    public async Task Login_WithoutAssignments_ReturnsCurrentOrganizationAsFallbackId()
    {
        var h = new LoginTestHarness(Now);
        var tenant = await CreateTenantAsync(h, "Tenant B", "tenant-b");
        var user = await CreateVerifiedAdminAsync(h, tenant.Id.Value, "admin@tenant-b.dev", "Password123!");

        var currentOrganizationId = Guid.NewGuid();
        var assignCurrent = user.AssignOrganization(currentOrganizationId, Now);
        Assert.True(assignCurrent.IsSuccess);
        h.Users.Update(user);

        var result = await h.Login.Handle(
            new LoginCommand("admin@tenant-b.dev", "Password123!"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.User.TenantIds);
        Assert.Single(result.Value.User.TenantIds!);
        Assert.Equal(currentOrganizationId.ToString(), result.Value.User.TenantIds![0]);
    }

    private static async Task<Tenant> CreateTenantAsync(LoginTestHarness h, string name, string key)
    {
        var tenantResult = Tenant.Create(
            TenantId.New(),
            name,
            key,
            "owner@zonar.dev",
            TenantPlanType.Starter,
            Now);

        Assert.True(tenantResult.IsSuccess);
        await h.Tenants.AddAsync(tenantResult.Value, CancellationToken.None);
        return tenantResult.Value;
    }

    private static async Task<User> CreateVerifiedAdminAsync(
        LoginTestHarness h,
        Guid tenantId,
        string email,
        string password)
    {
        var userResult = User.Register(
            UserId.New(),
            email,
            "Tenant Admin",
            phone: null,
            birthDate: null,
            passwordHash: h.Hasher.Hash(password),
            role: UserRole.Admin,
            tenantId: tenantId,
            nowUtc: Now);

        Assert.True(userResult.IsSuccess);

        var user = userResult.Value;
        user.SetVerificationCode("123456", Now.AddMinutes(10));

        var verified = user.VerifyEmail("123456", Now);
        Assert.True(verified.IsSuccess);

        await h.Users.AddAsync(user, CancellationToken.None);
        return user;
    }

    private sealed class LoginTestHarness
    {
        public LoginTestHarness(DateTime nowUtc)
        {
            Users = new InMemoryUserRepository(new InMemoryUserStore());
            Tenants = new InMemoryTenantRepository(new InMemoryTenantStore());
            Assignments = new InMemoryUserOrganizationAssignmentRepository(new InMemoryUserOrganizationAssignmentStore());
            Hasher = new PasswordHasher();

            var jwt = new StubJwtTokenService(nowUtc.AddMinutes(30));
            var clock = new TestClock(nowUtc);
            var unitOfWork = new InMemoryUnitOfWork();

            Login = new LoginHandler(Users, Assignments, Tenants, Hasher, jwt, unitOfWork, clock);
        }

        public InMemoryUserRepository Users { get; }
        public InMemoryTenantRepository Tenants { get; }
        public InMemoryUserOrganizationAssignmentRepository Assignments { get; }
        public PasswordHasher Hasher { get; }
        public LoginHandler Login { get; }
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
}
