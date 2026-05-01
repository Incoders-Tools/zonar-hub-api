namespace ZonarHub.Domain.Tournaments;

public readonly record struct TournamentId(Guid Value)
{
    public static TournamentId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
