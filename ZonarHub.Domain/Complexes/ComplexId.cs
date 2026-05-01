namespace ZonarHub.Domain.Complexes;

public readonly record struct ComplexId(Guid Value)
{
    public static ComplexId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
