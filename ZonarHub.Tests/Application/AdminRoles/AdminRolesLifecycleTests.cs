using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminRoles.Create;
using ZonarHub.Application.Features.AdminRoles.Delete;
using ZonarHub.Application.Features.AdminRoles.GetAll;
using ZonarHub.Application.Features.AdminRoles.GetById;
using ZonarHub.Application.Features.AdminRoles.Update;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.AdminRoles;

public class AdminRolesLifecycleTests
{
    private static readonly DateTime Now = new(2026, 6, 6, 11, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FullLifecycle_CreateReadUpdateDelete_Works()
    {
        var h = new AdminRolesTestHarness(Now);

        var created = await h.Create.Handle(
            new CreateAdminRoleCommand("ops_manager", "Operations manager", true),
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.False(created.Value.IsSystem);
        Assert.Equal("ops_manager", created.Value.Name);

        var fetched = await h.GetById.Handle(new GetAdminRoleByIdQuery(created.Value.Id), CancellationToken.None);
        Assert.True(fetched.IsSuccess);
        Assert.Equal(created.Value.Id, fetched.Value.Id);

        h.Clock.UtcNow = Now.AddMinutes(10);
        var updated = await h.Update.Handle(
            new UpdateAdminRoleCommand(created.Value.Id, "ops_manager_v2", "Updated description", false),
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.Equal("ops_manager_v2", updated.Value.Name);
        Assert.False(updated.Value.IsActive);
        Assert.Equal(Now.AddMinutes(10), updated.Value.UpdatedAt);

        var listed = await h.GetAll.Handle(new GetAdminRolesQuery(), CancellationToken.None);
        Assert.True(listed.IsSuccess);
        Assert.Contains(listed.Value, role => role.Id == created.Value.Id && role.Name == "ops_manager_v2");

        var deleted = await h.Delete.Handle(new DeleteAdminRoleCommand(created.Value.Id), CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        var missing = await h.GetById.Handle(new GetAdminRoleByIdQuery(created.Value.Id), CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal("admin_roles.not_found", missing.Error.Code);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        var h = new AdminRolesTestHarness(Now);

        var first = await h.Create.Handle(
            new CreateAdminRoleCommand("custom_support", "Custom support", true),
            CancellationToken.None);

        var duplicate = await h.Create.Handle(
            new CreateAdminRoleCommand("custom_support", "Duplicate", true),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(duplicate.IsFailure);
        Assert.Equal("admin_roles.name_exists", duplicate.Error.Code);
    }

    [Fact]
    public async Task Update_SystemRole_ReturnsFailure()
    {
        var h = new AdminRolesTestHarness(Now);

        var result = await h.Update.Handle(
            new UpdateAdminRoleCommand("role001", "root", "Root role", true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("admin_roles.immutable_system_role", result.Error.Code);
    }

    [Fact]
    public async Task Delete_SystemRole_ReturnsFailure()
    {
        var h = new AdminRolesTestHarness(Now);

        var result = await h.Delete.Handle(new DeleteAdminRoleCommand("role002"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("admin_roles.immutable_system_role", result.Error.Code);
    }
}

internal sealed class AdminRolesTestHarness
{
    public AdminRolesTestHarness(DateTime nowUtc)
    {
        Clock = new TestClock(nowUtc);
        UnitOfWork = new InMemoryUnitOfWork();
        Roles = new InMemoryRoleRepository(nowUtc);

        Create = new CreateAdminRoleHandler(Roles, UnitOfWork, Clock);
        Update = new UpdateAdminRoleHandler(Roles, UnitOfWork, Clock);
        Delete = new DeleteAdminRoleHandler(Roles, UnitOfWork);
        GetAll = new GetAdminRolesHandler(Roles);
        GetById = new GetAdminRoleByIdHandler(Roles);
    }

    public TestClock Clock { get; }
    public InMemoryUnitOfWork UnitOfWork { get; }
    public InMemoryRoleRepository Roles { get; }

    public CreateAdminRoleHandler Create { get; }
    public UpdateAdminRoleHandler Update { get; }
    public DeleteAdminRoleHandler Delete { get; }
    public GetAdminRolesHandler GetAll { get; }
    public GetAdminRoleByIdHandler GetById { get; }
}

internal sealed class InMemoryRoleRepository : IRoleRepository
{
    private readonly List<RoleDefinition> _items;

    public InMemoryRoleRepository(DateTime nowUtc)
    {
        _items =
        [
            new RoleDefinition("role001", "system_admin", "System admin", true, true, nowUtc, nowUtc),
            new RoleDefinition("role002", "admin", "Tenant admin", true, true, nowUtc, nowUtc),
            new RoleDefinition("role003", "viewer", "Read only", true, true, nowUtc, nowUtc),
            new RoleDefinition("role004", "editor", "Editor", true, true, nowUtc, nowUtc),
            new RoleDefinition("role005", "user", "Default user", true, true, nowUtc, nowUtc),
            new RoleDefinition("role006", "player", "Player", true, true, nowUtc, nowUtc)
        ];
    }

    public Task<IReadOnlyList<RoleDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RoleDefinition> items = _items
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(items);
    }

    public Task<RoleDefinition?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var role = _items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(role);
    }

    public Task<RoleDefinition?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = (name ?? string.Empty).Trim().ToLowerInvariant();
        var role = _items.FirstOrDefault(item => string.Equals(item.Name, normalized, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(role);
    }

    public Task AddAsync(RoleDefinition role, CancellationToken cancellationToken = default)
    {
        _items.Add(role);
        return Task.CompletedTask;
    }

    public void Update(RoleDefinition role)
    {
        var index = _items.FindIndex(item => string.Equals(item.Id, role.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _items[index] = role;
        }
    }

    public Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        _items.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }
}
