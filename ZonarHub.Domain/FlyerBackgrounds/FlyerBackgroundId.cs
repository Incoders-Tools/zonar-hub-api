namespace ZonarHub.Domain.FlyerBackgrounds;

public readonly record struct FlyerBackgroundId(Guid Value)
{
    public static FlyerBackgroundId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
