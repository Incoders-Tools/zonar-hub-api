namespace ZonarHub.Domain.Registrations;

/// <summary>
/// Places of a tournament at one moment (D2): <see cref="MaxPairs"/> limits <see cref="RegistrationStatus.Confirmed"/>
/// registrations; null or a value below 1 means no limit.
/// </summary>
/// <param name="Confirmed">Confirmed registrations of the tournament.</param>
/// <param name="Pending">Pending registrations of the tournament.</param>
public readonly record struct RegistrationCapacity(int? MaxPairs, int Confirmed, int Pending)
{
    /// <summary>True when <see cref="MaxPairs"/> is a positive limit.</summary>
    public bool IsLimited => MaxPairs is > 0;

    /// <summary>One more registration can be confirmed.</summary>
    public bool HasConfirmedPlace => !IsLimited || Confirmed < MaxPairs;

    /// <summary>Confirmed plus pending already reach the limit, so a new submission is waitlisted.</summary>
    public bool IsFullForNewEntries => IsLimited && Confirmed + Pending >= MaxPairs;

    /// <summary>Places still free for confirmation (never negative); null without a limit.</summary>
    public int? PlacesLeft => IsLimited ? Math.Max(0, MaxPairs!.Value - Confirmed) : null;
}
