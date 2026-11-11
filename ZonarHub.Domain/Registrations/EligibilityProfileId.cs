namespace ZonarHub.Domain.Registrations;

public readonly record struct EligibilityProfileId(Guid Value)
{
    public static EligibilityProfileId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
