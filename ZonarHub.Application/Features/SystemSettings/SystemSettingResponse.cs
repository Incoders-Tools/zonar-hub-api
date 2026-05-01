using ZonarHub.Domain.SystemSettings;

namespace ZonarHub.Application.Features.SystemSettings;

public sealed record SystemSettingResponse(
    Guid Id,
    string Key,
    string Value,
    SystemSettingScope Scope,
    Guid? TenantId,
    Guid? UserId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static SystemSettingResponse FromDomain(SystemSetting setting) =>
        new(
            setting.Id.Value,
            setting.Key,
            setting.Value,
            setting.Scope,
            setting.TenantId,
            setting.UserId,
            setting.CreatedAtUtc,
            setting.UpdatedAtUtc);
}
