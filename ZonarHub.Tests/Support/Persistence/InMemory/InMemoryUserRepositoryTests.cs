using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Users;
using Xunit;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public class InMemoryUserRepositoryTests
{
    private static readonly DateTime SeedUtc = new(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task List_WithExcludeRole_RemovesRoleBeforeCountAndPaging()
    {
        var tenantId = Guid.NewGuid();
        var store = new InMemoryUserStore();
        Seed(store, "a-sys@example.test", UserRole.SystemAdmin, tenantId);
        Seed(store, "b-admin@example.test", UserRole.Admin, tenantId);
        Seed(store, "c-sys@example.test", UserRole.SystemAdmin, tenantId);
        Seed(store, "d-editor@example.test", UserRole.Editor, tenantId);
        Seed(store, "e-other-tenant@example.test", UserRole.Editor, Guid.NewGuid());
        var repository = new InMemoryUserRepository(store);

        var (items, totalCount) = await repository.ListAsync(new UserQuery(
            tenantId, null, null, null, 1, 1, ExcludeRole: UserRole.SystemAdmin));

        Assert.Equal(2, totalCount);
        var first = Assert.Single(items);
        Assert.Equal("b-admin@example.test", first.Email);

        var (secondPage, _) = await repository.ListAsync(new UserQuery(
            tenantId, null, null, null, 2, 1, ExcludeRole: UserRole.SystemAdmin));
        Assert.Equal("d-editor@example.test", Assert.Single(secondPage).Email);
    }

    [Fact]
    public async Task List_WithoutExcludeRole_KeepsAllRoles()
    {
        var store = new InMemoryUserStore();
        Seed(store, "a-sys@example.test", UserRole.SystemAdmin, null);
        Seed(store, "b-admin@example.test", UserRole.Admin, null);
        var repository = new InMemoryUserRepository(store);

        var (items, totalCount) = await repository.ListAsync(new UserQuery(null, null, null, null, 1, 10));

        Assert.Equal(2, totalCount);
        Assert.Equal(["a-sys@example.test", "b-admin@example.test"], items.Select(u => u.Email));
    }

    [Fact]
    public async Task List_WithExcludeRoleAndMembership_FailsClosedLikeProvider()
    {
        var repository = new InMemoryUserRepository(new InMemoryUserStore());

        await Assert.ThrowsAsync<NotSupportedException>(() => repository.ListAsync(new UserQuery(
            null, null, null, null, 1, 10,
            new UserOrganizationMembership(Guid.NewGuid(), null),
            UserRole.SystemAdmin)));
    }

    private static void Seed(InMemoryUserStore store, string email, UserRole role, Guid? tenantId)
    {
        var user = User.Reconstitute(
            UserId.New(), email, email, null, null, "seed-hash", role, tenantId, null,
            null, null, null, true, true, SeedUtc, SeedUtc, null, null, null, null, null, null);
        store.Data[user.Id] = user;
    }
}
