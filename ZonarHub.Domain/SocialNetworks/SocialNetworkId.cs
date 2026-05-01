namespace ZonarHub.Domain.SocialNetworks;

public readonly record struct SocialNetworkId(Guid Value)
{
    public static SocialNetworkId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
