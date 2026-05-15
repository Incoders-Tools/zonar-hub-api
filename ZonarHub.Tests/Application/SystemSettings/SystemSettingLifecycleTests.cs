using ZonarHub.Application.Features.SystemSettings.Create;
using ZonarHub.Application.Features.SystemSettings.Delete;
using ZonarHub.Application.Features.SystemSettings.GetAll;
using ZonarHub.Application.Features.SystemSettings.GetById;
using ZonarHub.Application.Features.SystemSettings.Update;
using ZonarHub.Domain.SystemSettings;

namespace ZonarHub.Tests.Application.SystemSettings;

public class SystemSettingLifecycleTests
{
    private static readonly DateTime Now = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FullLifecycle_CreateReadUpdateDelete_Works()
    {
        var h = new SystemSettingsTestHarness(Now);

        var created = await h.Create.Handle(
            new CreateSystemSettingCommand("ui.theme", "dark", SystemSettingScope.Global, null, null),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var fetched = await h.GetById.Handle(new GetSystemSettingByIdQuery(created.Value.Id), CancellationToken.None);
        Assert.True(fetched.IsSuccess);
        Assert.Equal(created.Value.Id, fetched.Value.Id);

        h.Clock.UtcNow = Now.AddHours(1);
        var updated = await h.Update.Handle(
            new UpdateSystemSettingCommand(created.Value.Id, "light", SystemSettingScope.Global, null, null),
            CancellationToken.None);
        Assert.True(updated.IsSuccess);
        Assert.Equal("light", updated.Value.Value);
        Assert.Equal(Now.AddHours(1), updated.Value.UpdatedAtUtc);

        var deleted = await h.Delete.Handle(new DeleteSystemSettingCommand(created.Value.Id), CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        var missing = await h.GetById.Handle(new GetSystemSettingByIdQuery(created.Value.Id), CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal("system_settings.not_found", missing.Error.Code);
    }

    [Fact]
    public async Task Create_DuplicateKeyWithinSameScope_UpdatesExistingSetting()
    {
        var h = new SystemSettingsTestHarness(Now);
        var first = await h.Create.Handle(
            new CreateSystemSettingCommand("dup", "one", SystemSettingScope.Global, null, null),
            CancellationToken.None);

        Assert.True(first.IsSuccess);

        h.Clock.UtcNow = Now.AddMinutes(5);

        var duplicate = await h.Create.Handle(
            new CreateSystemSettingCommand("dup", "two", SystemSettingScope.Global, null, null),
            CancellationToken.None);

        Assert.True(duplicate.IsSuccess);
        Assert.Equal(first.Value.Id, duplicate.Value.Id);
        Assert.Equal("two", duplicate.Value.Value);
        Assert.Equal(Now.AddMinutes(5), duplicate.Value.UpdatedAtUtc);
    }
}
