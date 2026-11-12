using ZonarHub.Domain.Players;

namespace ZonarHub.Domain.Registrations;

/// <summary>A player in one slot (1-based, in the order of the modality's slots) of a registration.</summary>
public sealed record RegistrationParticipant(int SlotNumber, PlayerId PlayerId)
{
    /// <summary>Highest slot number; also the largest team a registration can carry.</summary>
    public const int MaxSlotNumber = 16;
}
