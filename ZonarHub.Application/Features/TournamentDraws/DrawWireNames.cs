using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Draws;
using ZonarHub.Domain.Draws.Results;

namespace ZonarHub.Application.Features.TournamentDraws;

/// <summary>
/// The single camelCase spelling of every draw enum value (L7), shared by the stored columns and jsonb documents and by
/// the API contracts, so no global enum converter is needed. Parsing is exact (ordinal, case-sensitive).
/// </summary>
public static class DrawWireNames
{
    /// <summary>Kinds of a serialized <see cref="Slot"/>.</summary>
    public static class SlotKinds
    {
        public const string Participant = "participant";
        public const string Bye = "bye";
        public const string WinnerOf = "winnerOf";
        public const string LoserOf = "loserOf";
        public const string GroupPosition = "groupPosition";
    }

    private static readonly WireMap<DrawFormat> Formats = new(
        (DrawFormat.SingleElimination, "singleElimination"),
        (DrawFormat.RoundRobin, "roundRobin"),
        (DrawFormat.GroupsThenKnockout, "groupsThenKnockout"));

    private static readonly WireMap<ParticipationFormat> ParticipationFormats = new(
        (ParticipationFormat.Individual, "individual"),
        (ParticipationFormat.Pair, "pair"),
        (ParticipationFormat.Team, "team"));

    private static readonly WireMap<RandomizationMode> RandomizationModes = new(
        (RandomizationMode.Random, "random"),
        (RandomizationMode.Seeded, "seeded"));

    private static readonly WireMap<PhaseKind> PhaseKinds = new(
        (PhaseKind.GroupStage, "groupStage"),
        (PhaseKind.Knockout, "knockout"),
        (PhaseKind.RoundRobin, "roundRobin"));

    private static readonly WireMap<SeparationConflictKind> ConflictKinds = new(
        (SeparationConflictKind.FirstRoundMatch, "firstRoundMatch"),
        (SeparationConflictKind.SameGroup, "sameGroup"));

    private static readonly WireMap<MatchStatus> MatchStatuses = new(
        (MatchStatus.Scheduled, "scheduled"),
        (MatchStatus.InProgress, "inProgress"),
        (MatchStatus.Completed, "completed"),
        (MatchStatus.Walkover, "walkover"),
        (MatchStatus.Retired, "retired"));

    private static readonly WireMap<MatchSide> MatchSides = new(
        (MatchSide.Home, "home"),
        (MatchSide.Away, "away"));

    private static readonly WireMap<TournamentDrawStatus> DrawStatuses = new(
        (TournamentDrawStatus.Draft, "draft"),
        (TournamentDrawStatus.Published, "published"));

    public static string ToWireName(this DrawFormat value) => Formats.Name(value);

    public static string ToWireName(this ParticipationFormat value) => ParticipationFormats.Name(value);

    public static string ToWireName(this RandomizationMode value) => RandomizationModes.Name(value);

    public static string ToWireName(this PhaseKind value) => PhaseKinds.Name(value);

    public static string ToWireName(this SeparationConflictKind value) => ConflictKinds.Name(value);

    public static string ToWireName(this MatchStatus value) => MatchStatuses.Name(value);

    public static string ToWireName(this MatchSide value) => MatchSides.Name(value);

    public static string ToWireName(this TournamentDrawStatus value) => DrawStatuses.Name(value);

    public static bool TryParse(string? name, out DrawFormat value) => Formats.TryParse(name, out value);

    public static bool TryParse(string? name, out ParticipationFormat value) => ParticipationFormats.TryParse(name, out value);

    public static bool TryParse(string? name, out RandomizationMode value) => RandomizationModes.TryParse(name, out value);

    public static bool TryParse(string? name, out PhaseKind value) => PhaseKinds.TryParse(name, out value);

    public static bool TryParse(string? name, out SeparationConflictKind value) => ConflictKinds.TryParse(name, out value);

    public static bool TryParse(string? name, out MatchStatus value) => MatchStatuses.TryParse(name, out value);

    public static bool TryParse(string? name, out MatchSide value) => MatchSides.TryParse(name, out value);

    public static bool TryParse(string? name, out TournamentDrawStatus value) => DrawStatuses.TryParse(name, out value);

    private sealed class WireMap<TEnum>
        where TEnum : struct, Enum
    {
        private readonly Dictionary<TEnum, string> _names;
        private readonly Dictionary<string, TEnum> _values;

        public WireMap(params (TEnum Value, string Name)[] pairs)
        {
            _names = pairs.ToDictionary(pair => pair.Value, pair => pair.Name);
            _values = pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
        }

        /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not a defined value.</exception>
        public string Name(TEnum value) =>
            _names.TryGetValue(value, out var name)
                ? name
                : throw new ArgumentOutOfRangeException(nameof(value), value, $"No wire name for {typeof(TEnum).Name} {value}.");

        public bool TryParse(string? name, out TEnum value)
        {
            if (name is not null && _values.TryGetValue(name, out value))
                return true;

            value = default;
            return false;
        }
    }
}
