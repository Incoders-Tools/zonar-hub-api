namespace ZonarHub.Domain.Registrations;

public readonly record struct TournamentRegistrationId(Guid Value)
{
    public static TournamentRegistrationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
