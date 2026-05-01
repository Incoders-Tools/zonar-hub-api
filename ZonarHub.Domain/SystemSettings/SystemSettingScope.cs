namespace ZonarHub.Domain.SystemSettings;

/// <summary>
/// Ownership boundary for a <see cref="SystemSetting"/>.
/// </summary>
public enum SystemSettingScope
{
    Global = 0,
    Tenant = 1,
    User = 2,
}
