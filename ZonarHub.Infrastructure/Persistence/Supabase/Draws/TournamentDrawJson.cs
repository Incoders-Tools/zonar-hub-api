using System.Buffers;
using System.Globalization;
using System.Text.Json;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Domain.Draws;
using ZonarHub.Domain.Draws.Results;

namespace ZonarHub.Infrastructure.Persistence.Supabase.Draws;

/// <summary>
/// Stored jsonb documents of a tournament draw (D2), kept apart from the pure domain. The <c>structure</c>,
/// <c>request</c> and <c>options</c> documents carry <c>"version": 1</c>; a missing or other version is rejected so a
/// future shape needs an explicit reader. Shape: camelCase properties in a fixed order, every property always present
/// (absent values are <c>null</c>, empty lists <c>[]</c>), enums as <see cref="DrawWireNames"/>, and seeds as decimal
/// strings because a 64-bit unsigned seed does not fit a JSON number safely read by every client.
/// </summary>
/// <remarks>
/// <para><c>structure</c>: <c>{ version, format, participationFormat, randomizationMode, seed, participants, phases,
/// separationConflicts }</c>; a phase is <c>{ id, kind, groups, rounds }</c>, a group <c>{ id, participantIds, rounds }</c>,
/// a round <c>{ number, name, restingParticipantIds, matches }</c>, a match <c>{ id, roundNumber, number, home, away,
/// winnerAdvancesTo, loserAdvancesTo }</c> and a slot <c>{ kind, participantId | matchId | groupId + rank }</c>. A
/// group stage stores its merged matchday rounds and each group's own rounds, exactly as generated.</para>
/// <para><c>request</c>: <c>{ version, format, participationFormat, randomization: { mode, seed }, participants,
/// options, scoring }</c>, as submitted (seed and options may be null).</para>
/// <para><c>options</c>: <c>{ version, knockout, roundRobin, groups, scoring }</c>.</para>
/// <para><c>sets</c> (result column, no version): <c>[{ home, away, tiebreak: { home, away } | null }]</c>.</para>
/// Malformed documents throw <see cref="JsonException"/>.
/// </remarks>
internal static class TournamentDrawJson
{
    public const int Version = 1;

    private delegate bool Parser<T>(string? name, out T value);

    public static JsonElement WriteDraw(Draw draw)
    {
        ArgumentNullException.ThrowIfNull(draw);

        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteString("format", draw.Format.ToWireName());
            writer.WriteString("participationFormat", draw.ParticipationFormat.ToWireName());
            writer.WriteString("randomizationMode", draw.RandomizationMode.ToWireName());
            writer.WriteString("seed", FormatSeed(draw.Seed));
            WriteParticipants(writer, draw.Participants);

            writer.WriteStartArray("phases");
            foreach (var phase in draw.Phases)
            {
                writer.WriteStartObject();
                writer.WriteString("id", phase.Id);
                writer.WriteString("kind", phase.Kind.ToWireName());
                writer.WriteStartArray("groups");
                foreach (var group in phase.Groups)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", group.Id);
                    WriteStrings(writer, "participantIds", group.ParticipantIds);
                    WriteRounds(writer, group.Rounds);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                WriteRounds(writer, phase.Rounds);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("separationConflicts");
            foreach (var conflict in draw.SeparationConflicts)
            {
                writer.WriteStartObject();
                writer.WriteString("key", conflict.Key);
                WriteStrings(writer, "participantIds", conflict.ParticipantIds);
                writer.WriteString("kind", conflict.Kind.ToWireName());
                writer.WriteString("locationId", conflict.LocationId);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    public static Draw ReadDraw(JsonElement element)
    {
        var root = VersionedRoot(element);

        return new Draw(
            Enum<DrawFormat>(root, "format", DrawWireNames.TryParse),
            Enum<ParticipationFormat>(root, "participationFormat", DrawWireNames.TryParse),
            Enum<RandomizationMode>(root, "randomizationMode", DrawWireNames.TryParse),
            ParseSeed(String(root, "seed")),
            ReadParticipants(root),
            List(root, "phases", phase => new Phase(
                String(phase, "id"),
                Enum<PhaseKind>(phase, "kind", DrawWireNames.TryParse),
                ReadRounds(phase))
            {
                Groups = List(phase, "groups", group => new Group(
                    String(group, "id"),
                    Strings(group, "participantIds"),
                    ReadRounds(group))),
            }))
        {
            SeparationConflicts = List(root, "separationConflicts", conflict => new SeparationConflict(
                String(conflict, "key"),
                Strings(conflict, "participantIds"),
                Enum<SeparationConflictKind>(conflict, "kind", DrawWireNames.TryParse),
                String(conflict, "locationId"))),
        };
    }

    public static JsonElement WriteRequest(DrawRequest request, ScoringRules scoring)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(scoring);

        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteString("format", request.Format.ToWireName());
            writer.WriteString("participationFormat", request.ParticipationFormat.ToWireName());
            writer.WriteStartObject("randomization");
            writer.WriteString("mode", request.Randomization.Mode.ToWireName());
            if (request.Randomization.Seed is ulong seed)
                writer.WriteString("seed", FormatSeed(seed));
            else
                writer.WriteNull("seed");
            writer.WriteEndObject();
            WriteParticipants(writer, request.Participants);
            writer.WritePropertyName("options");
            if (request.Options is null)
                writer.WriteNullValue();
            else
            {
                writer.WriteStartObject();
                WriteOptionSections(writer, request.Options);
                writer.WriteEndObject();
            }

            WriteScoring(writer, scoring);
            writer.WriteEndObject();
        });
    }

    public static (DrawRequest Request, ScoringRules Scoring) ReadRequest(JsonElement element)
    {
        var root = VersionedRoot(element);
        var randomization = Object(root, "randomization");
        var seed = NullableString(randomization, "seed");
        var options = Property(root, "options");

        var request = new DrawRequest(
            Enum<DrawFormat>(root, "format", DrawWireNames.TryParse),
            Enum<ParticipationFormat>(root, "participationFormat", DrawWireNames.TryParse),
            ReadParticipants(root),
            new RandomizationPolicy(
                Enum<RandomizationMode>(randomization, "mode", DrawWireNames.TryParse),
                seed is null ? null : ParseSeed(seed)),
            options.ValueKind == JsonValueKind.Null ? null : ReadOptionSections(AsObject(options, "options")));

        return (request, ReadScoring(root));
    }

    public static JsonElement WriteOptions(DrawOptions? options, ScoringRules scoring)
    {
        ArgumentNullException.ThrowIfNull(scoring);

        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            WriteOptionSections(writer, options ?? new DrawOptions());
            WriteScoring(writer, scoring);
            writer.WriteEndObject();
        });
    }

    public static (DrawOptions Options, ScoringRules Scoring) ReadOptions(JsonElement element)
    {
        var root = VersionedRoot(element);
        return (ReadOptionSections(root), ReadScoring(root));
    }

    public static JsonElement WriteSets(IReadOnlyList<SetScore> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);

        return Write(writer =>
        {
            writer.WriteStartArray();
            foreach (var set in sets)
            {
                writer.WriteStartObject();
                writer.WriteNumber("home", set.Home);
                writer.WriteNumber("away", set.Away);
                writer.WritePropertyName("tiebreak");
                if (set.Tiebreak is null)
                    writer.WriteNullValue();
                else
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("home", set.Tiebreak.Home);
                    writer.WriteNumber("away", set.Tiebreak.Away);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    public static IReadOnlyList<SetScore> ReadSets(JsonElement element) =>
        Items(element, "sets", set =>
        {
            var tiebreak = Property(set, "tiebreak");
            return new SetScore(
                Int(set, "home"),
                Int(set, "away"),
                tiebreak.ValueKind == JsonValueKind.Null
                    ? null
                    : new TiebreakScore(Int(AsObject(tiebreak, "tiebreak"), "home"), Int(tiebreak, "away")));
        });

    private static JsonElement Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            write(writer);

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void WriteParticipants(Utf8JsonWriter writer, IReadOnlyList<DrawParticipant> participants)
    {
        writer.WriteStartArray("participants");
        foreach (var participant in participants)
        {
            writer.WriteStartObject();
            writer.WriteString("id", participant.Id);
            writer.WriteString("displayName", participant.DisplayName);
            WriteStrings(writer, "members", participant.Members);
            WriteNullableNumber(writer, "seedRank", participant.SeedRank);
            writer.WriteString("separationKey", participant.SeparationKey);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static IReadOnlyList<DrawParticipant> ReadParticipants(JsonElement owner) =>
        List(owner, "participants", participant => new DrawParticipant(
            String(participant, "id"),
            String(participant, "displayName"),
            Strings(participant, "members"),
            NullableInt(participant, "seedRank"),
            NullableString(participant, "separationKey")));

    private static void WriteRounds(Utf8JsonWriter writer, IReadOnlyList<Round> rounds)
    {
        writer.WriteStartArray("rounds");
        foreach (var round in rounds)
        {
            writer.WriteStartObject();
            writer.WriteNumber("number", round.Number);
            writer.WriteString("name", round.Name);
            WriteStrings(writer, "restingParticipantIds", round.RestingParticipantIds);
            writer.WriteStartArray("matches");
            foreach (var match in round.Matches)
            {
                writer.WriteStartObject();
                writer.WriteString("id", match.Id);
                writer.WriteNumber("roundNumber", match.RoundNumber);
                writer.WriteNumber("number", match.Number);
                WriteSlot(writer, "home", match.Home);
                WriteSlot(writer, "away", match.Away);
                writer.WriteString("winnerAdvancesTo", match.WinnerAdvancesTo);
                writer.WriteString("loserAdvancesTo", match.LoserAdvancesTo);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static IReadOnlyList<Round> ReadRounds(JsonElement owner) =>
        List(owner, "rounds", round => new Round(
            Int(round, "number"),
            String(round, "name"),
            List(round, "matches", match => new Match(
                String(match, "id"),
                Int(match, "roundNumber"),
                Int(match, "number"),
                ReadSlot(Object(match, "home")),
                ReadSlot(Object(match, "away")),
                NullableString(match, "winnerAdvancesTo"),
                NullableString(match, "loserAdvancesTo"))))
        {
            RestingParticipantIds = Strings(round, "restingParticipantIds"),
        });

    private static void WriteSlot(Utf8JsonWriter writer, string name, Slot slot)
    {
        writer.WriteStartObject(name);
        switch (slot)
        {
            case ParticipantSlot participant:
                writer.WriteString("kind", DrawWireNames.SlotKinds.Participant);
                writer.WriteString("participantId", participant.ParticipantId);
                break;
            case ByeSlot:
                writer.WriteString("kind", DrawWireNames.SlotKinds.Bye);
                break;
            case WinnerOfSlot winnerOf:
                writer.WriteString("kind", DrawWireNames.SlotKinds.WinnerOf);
                writer.WriteString("matchId", winnerOf.MatchId);
                break;
            case LoserOfSlot loserOf:
                writer.WriteString("kind", DrawWireNames.SlotKinds.LoserOf);
                writer.WriteString("matchId", loserOf.MatchId);
                break;
            default:
                // Slot has a private protected constructor: GroupPositionSlot is the only remaining kind.
                var position = (GroupPositionSlot)slot;
                writer.WriteString("kind", DrawWireNames.SlotKinds.GroupPosition);
                writer.WriteString("groupId", position.GroupId);
                writer.WriteNumber("rank", position.Rank);
                break;
        }

        writer.WriteEndObject();
    }

    private static Slot ReadSlot(JsonElement slot) => String(slot, "kind") switch
    {
        DrawWireNames.SlotKinds.Participant => new ParticipantSlot(String(slot, "participantId")),
        DrawWireNames.SlotKinds.Bye => Slot.Bye,
        DrawWireNames.SlotKinds.WinnerOf => new WinnerOfSlot(String(slot, "matchId")),
        DrawWireNames.SlotKinds.LoserOf => new LoserOfSlot(String(slot, "matchId")),
        DrawWireNames.SlotKinds.GroupPosition => new GroupPositionSlot(String(slot, "groupId"), Int(slot, "rank")),
        var kind => throw Invalid($"unknown slot kind '{kind}'"),
    };

    private static void WriteOptionSections(Utf8JsonWriter writer, DrawOptions options)
    {
        writer.WritePropertyName("knockout");
        if (options.Knockout is null)
            writer.WriteNullValue();
        else
        {
            writer.WriteStartObject();
            writer.WriteBoolean("thirdPlaceMatch", options.Knockout.ThirdPlaceMatch);
            writer.WriteEndObject();
        }

        writer.WritePropertyName("roundRobin");
        if (options.RoundRobin is null)
            writer.WriteNullValue();
        else
        {
            writer.WriteStartObject();
            writer.WriteNumber("legs", options.RoundRobin.Legs);
            writer.WriteEndObject();
        }

        writer.WritePropertyName("groups");
        if (options.Groups is null)
            writer.WriteNullValue();
        else
        {
            writer.WriteStartObject();
            WriteNullableNumber(writer, "groupCount", options.Groups.GroupCount);
            WriteNullableNumber(writer, "groupSize", options.Groups.GroupSize);
            writer.WriteNumber("qualifiersPerGroup", options.Groups.QualifiersPerGroup);
            writer.WriteEndObject();
        }
    }

    private static DrawOptions ReadOptionSections(JsonElement owner) => new(
        NullableObject(owner, "knockout", knockout => new KnockoutOptions(Bool(knockout, "thirdPlaceMatch"))),
        NullableObject(owner, "roundRobin", roundRobin => new RoundRobinOptions(Int(roundRobin, "legs"))),
        NullableObject(owner, "groups", groups => new GroupStageOptions(
            NullableInt(groups, "groupCount"),
            NullableInt(groups, "groupSize"),
            Int(groups, "qualifiersPerGroup"))));

    private static void WriteScoring(Utf8JsonWriter writer, ScoringRules scoring)
    {
        writer.WriteStartObject("scoring");
        writer.WriteNumber("bestOf", scoring.BestOf);
        writer.WriteBoolean("thirdSetSuperTiebreak", scoring.ThirdSetSuperTiebreak);
        writer.WriteEndObject();
    }

    private static ScoringRules ReadScoring(JsonElement owner)
    {
        var scoring = Object(owner, "scoring");
        try
        {
            return new ScoringRules(Int(scoring, "bestOf"), Bool(scoring, "thirdSetSuperTiebreak"));
        }
        catch (ArgumentOutOfRangeException error)
        {
            throw new JsonException($"Invalid stored draw document: {error.Message}", error);
        }
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is int number)
            writer.WriteNumber(name, number);
        else
            writer.WriteNull(name);
    }

    private static string FormatSeed(ulong seed) => seed.ToString(CultureInfo.InvariantCulture);

    private static ulong ParseSeed(string seed) =>
        ulong.TryParse(seed, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Invalid($"invalid seed '{seed}'");

    private static JsonElement VersionedRoot(JsonElement element)
    {
        var root = AsObject(element, "document");
        var version = Property(root, "version");
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != Version)
            throw new JsonException($"Unsupported stored draw document version {version.GetRawText()}; expected {Version}.");

        return root;
    }

    private static JsonElement Property(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) ? value : throw Invalid($"missing '{name}'");

    private static JsonElement AsObject(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object ? element : throw Invalid($"'{name}' must be an object");

    private static JsonElement Object(JsonElement owner, string name) => AsObject(Property(owner, name), name);

    private static T? NullableObject<T>(JsonElement owner, string name, Func<JsonElement, T> read)
        where T : class
    {
        var value = Property(owner, name);
        return value.ValueKind == JsonValueKind.Null ? null : read(AsObject(value, name));
    }

    private static string String(JsonElement owner, string name) =>
        NullableString(owner, name) ?? throw Invalid($"'{name}' must be a string");

    private static string? NullableString(JsonElement owner, string name)
    {
        var value = Property(owner, name);
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            _ => throw Invalid($"'{name}' must be a string or null"),
        };
    }

    private static int Int(JsonElement owner, string name) =>
        NullableInt(owner, name) ?? throw Invalid($"'{name}' must be a number");

    private static int? NullableInt(JsonElement owner, string name)
    {
        var value = Property(owner, name);
        if (value.ValueKind == JsonValueKind.Null)
            return null;

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw Invalid($"'{name}' must be a 32-bit integer or null");
    }

    private static bool Bool(JsonElement owner, string name) => Property(owner, name).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw Invalid($"'{name}' must be a boolean"),
    };

    private static T Enum<T>(JsonElement owner, string name, Parser<T> parse)
    {
        var text = String(owner, name);
        return parse(text, out var value) ? value : throw Invalid($"unknown {name} '{text}'");
    }

    private static IReadOnlyList<string> Strings(JsonElement owner, string name)
    {
        var values = Property(owner, name);
        if (values.ValueKind != JsonValueKind.Array)
            throw Invalid($"'{name}' must be an array");

        return values.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Invalid($"'{name}' must hold strings"))
            .ToList()
            .AsReadOnly();
    }

    private static IReadOnlyList<T> List<T>(JsonElement owner, string name, Func<JsonElement, T> read) =>
        Items(Property(owner, name), name, read);

    private static IReadOnlyList<T> Items<T>(JsonElement values, string name, Func<JsonElement, T> read)
    {
        if (values.ValueKind != JsonValueKind.Array)
            throw Invalid($"'{name}' must be an array");

        return values.EnumerateArray().Select(item => read(AsObject(item, name))).ToList().AsReadOnly();
    }

    private static JsonException Invalid(string reason) => new($"Invalid stored draw document: {reason}.");
}
