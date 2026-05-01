namespace ZonarHub.Domain.SystemSettings;

public readonly record struct SystemSettingId(Guid Value)
{
    public static SystemSettingId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
