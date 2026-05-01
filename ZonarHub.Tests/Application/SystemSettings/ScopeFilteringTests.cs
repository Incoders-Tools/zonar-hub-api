using ZonarHub.Application.Features.SystemSettings.Create;
using ZonarHub.Application.Features.SystemSettings.GetAll;
using ZonarHub.Domain.SystemSettings;

namespace ZonarHub.Tests.Application.SystemSettings;

public class ScopeFilteringTests
{
    private static readonly DateTime Now = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid UserA = Guid.NewGuid();

    [Fact]
    public async Task List_FilterByScope_ReturnsOnlyMatchingScope()
    {
        var h = new SystemSettingsTestHarness(Now);
        await Seed(h);

        var globals = await h.List.Handle(
            new GetSystemSettingsQuery(new SystemSettingsFilter(Scope: SystemSettingScope.Global)),
            CancellationToken.None);
        Assert.All(globals.Value.Items, s => Assert.Equal(SystemSettingScope.Global, s.Scope));
    }

    [Fact]
    public async Task List_FilterByTenantId_ReturnsOnlyThatTenantsSettings()
    {
        var h = new SystemSettingsTestHarness(Now);
        await Seed(h);

        var tenantA = await h.List.Handle(
            new GetSystemSettingsQuery(new SystemSettingsFilter(TenantId: TenantA)),
            CancellationToken.None);
        Assert.All(tenantA.Value.Items, s => Assert.Equal(TenantA, s.TenantId));
    }

    [Fact]
    public async Task List_FilterByUserId_ReturnsOnlyThatUsersSettings()
    {
        var h = new SystemSettingsTestHarness(Now);
        await Seed(h);

        var userScoped = await h.List.Handle(
            new GetSystemSettingsQuery(new SystemSettingsFilter(UserId: UserA)),
            CancellationToken.None);
        Assert.All(userScoped.Value.Items, s => Assert.Equal(UserA, s.UserId));
    }

    private static async Task Seed(SystemSettingsTestHarness h)
    {
        await h.Create.Handle(new CreateSystemSettingCommand("global.a", "v", SystemSettingScope.Global, null, null), CancellationToken.None);
        await h.Create.Handle(new CreateSystemSettingCommand("tenant.a", "v", SystemSettingScope.Tenant, TenantA, null), CancellationToken.None);
        await h.Create.Handle(new CreateSystemSettingCommand("tenant.b", "v", SystemSettingScope.Tenant, TenantB, null), CancellationToken.None);
        await h.Create.Handle(new CreateSystemSettingCommand("user.a", "v", SystemSettingScope.User, TenantA, UserA), CancellationToken.None);
    }
}
