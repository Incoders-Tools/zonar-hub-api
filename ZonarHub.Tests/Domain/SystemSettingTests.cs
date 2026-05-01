using ZonarHub.Domain.SystemSettings;

namespace ZonarHub.Tests.Domain;

public class SystemSettingTests
{
    private static readonly DateTime Now = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_Global_SucceedsWithoutTenantOrUser()
    {
        var result = SystemSetting.Create(SystemSettingId.New(), "theme.mode", "dark", SystemSettingScope.Global, null, null, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(SystemSettingScope.Global, result.Value.Scope);
        Assert.Null(result.Value.TenantId);
        Assert.Null(result.Value.UserId);
    }

    [Fact]
    public void Create_Global_RejectsTenantOrUser()
    {
        var result = SystemSetting.Create(SystemSettingId.New(), "k", "v", SystemSettingScope.Global, Guid.NewGuid(), null, Now);
        Assert.True(result.IsFailure);
        Assert.Equal("system_settings.scope_ownership_violation", result.Error.Code);
    }

    [Fact]
    public void Create_Tenant_RequiresTenantId()
    {
        var result = SystemSetting.Create(SystemSettingId.New(), "k", "v", SystemSettingScope.Tenant, null, null, Now);
        Assert.True(result.IsFailure);
        Assert.Equal("system_settings.tenant_required", result.Error.Code);
    }

    [Fact]
    public void Create_User_RequiresUserId()
    {
        var result = SystemSetting.Create(SystemSettingId.New(), "k", "v", SystemSettingScope.User, Guid.NewGuid(), null, Now);
        Assert.True(result.IsFailure);
        Assert.Equal("system_settings.user_required", result.Error.Code);
    }

    [Theory]
    [InlineData("", "v", "system_settings.key_required")]
    [InlineData("k", "", "system_settings.value_required")]
    public void Create_InvalidInput_ReturnsExpectedCode(string key, string value, string expectedCode)
    {
        var result = SystemSetting.Create(SystemSettingId.New(), key, value, SystemSettingScope.Global, null, null, Now);
        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Fact]
    public void Update_AdvancesUpdatedAtAndNormalizesOwnership()
    {
        var tenantId = Guid.NewGuid();
        var created = SystemSetting.Create(SystemSettingId.New(), "flag.x", "on", SystemSettingScope.Tenant, tenantId, null, Now).Value;
        var later = Now.AddHours(1);

        var result = created.Update("off", SystemSettingScope.Tenant, tenantId, null, later);

        Assert.True(result.IsSuccess);
        Assert.Equal("off", created.Value);
        Assert.Equal(later, created.UpdatedAtUtc);
        Assert.Equal(Now, created.CreatedAtUtc);
        Assert.Equal(tenantId, created.TenantId);
    }
}
