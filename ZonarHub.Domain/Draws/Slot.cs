namespace ZonarHub.Domain.Draws;

/// <summary>
/// One side of a <see cref="Match"/>: who plays there, or where that player comes from.
/// </summary>
public abstract record Slot
{
    private protected Slot() { }

    /// <summary>The shared empty side of a bye match.</summary>
    public static ByeSlot Bye { get; } = new();
}

/// <summary>A known participant.</summary>
public sealed record ParticipantSlot(string ParticipantId) : Slot;

/// <summary>No opponent: the other side advances without playing.</summary>
public sealed record ByeSlot : Slot;

/// <summary>The winner of another match.</summary>
public sealed record WinnerOfSlot(string MatchId) : Slot;

/// <summary>The loser of another match (third-place match).</summary>
public sealed record LoserOfSlot(string MatchId) : Slot;

/// <summary>The participant finishing at <paramref name="Rank"/> (1-based) in a group.</summary>
public sealed record GroupPositionSlot(string GroupId, int Rank) : Slot;
