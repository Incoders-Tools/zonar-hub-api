namespace ZonarHub.Domain.Sports;

public readonly record struct SportId(Guid Value)
{
    public static SportId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
