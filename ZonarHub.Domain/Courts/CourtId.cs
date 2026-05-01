namespace ZonarHub.Domain.Courts;

public readonly record struct CourtId(Guid Value)
{
    public static CourtId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
