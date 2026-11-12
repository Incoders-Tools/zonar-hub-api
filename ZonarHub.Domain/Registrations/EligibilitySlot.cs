namespace ZonarHub.Domain.Registrations;

/// <summary>
/// Rules of one participant slot of an <see cref="EligibilityProfile"/> (D1). Null rules do not restrict. Ages are whole
/// years from the player's birth date; the D3 checks that apply them belong to the registration use cases.
/// </summary>
/// <param name="SlotNumber">1-based slot, matching <see cref="RegistrationParticipant.SlotNumber"/>.</param>
/// <param name="GenderId">Required gender (<c>genders</c> catalog).</param>
/// <param name="CategoryId">Required category (<c>categories</c> catalog), compared with the level tolerance rule.</param>
/// <param name="Label">Display label of the slot (e.g. "Player 1").</param>
/// <param name="Required">False when the slot may stay empty.</param>
public sealed record EligibilitySlot(
    int SlotNumber,
    Guid? GenderId,
    Guid? CategoryId,
    int? MinAge,
    int? MaxAge,
    string? Label,
    bool Required)
{
    /// <summary>Highest accepted <see cref="MinAge"/> or <see cref="MaxAge"/>.</summary>
    public const int AgeLimit = 120;

    public const int MaxLabelLength = 60;
}
