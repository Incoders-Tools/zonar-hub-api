using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Registrations;
using ZonarHub.Domain.Tournaments;
using ZonarHub.Infrastructure.Persistence.Supabase.Registrations;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

/// <summary>
/// PostgREST store of tournament registrations (migration <c>20261110100000_tournament_registrations</c>). Reads embed
/// the participants and their player summaries; every write is one call to a SQL function that locks the tournament,
/// applies the D2 rules and checks the expected revision in one transaction. A function refuses a write with SQLSTATE
/// <c>PT409</c> (HTTP 409) and a <c>registrations.*</c> message, mapped to <see cref="PersistenceConflictException"/>
/// through <see cref="RegistrationErrors.FromStoreCode"/>. Enum columns use <see cref="RegistrationWireNames"/>;
/// timestamps are written from <see cref="IClock"/> and read as UTC. Other store failures throw
/// <see cref="HttpRequestException"/>; stored values outside the contract throw <see cref="JsonException"/>.
/// </summary>
internal sealed class TournamentRegistrationRepository(IHttpClientFactory factory, IClock clock) : ITournamentRegistrationRepository
{
    private const string RestPath = "/rest/v1/tournament_registrations";
    private const string CreateRpc = "/rest/v1/rpc/create_tournament_registration";
    private const string TransitionRpc = "/rest/v1/rpc/transition_tournament_registration";
    private const string PaymentRpc = "/rest/v1/rpc/set_tournament_registration_payment_status";

    /// <summary>Same shape as the functions' <c>tournament_registration_document</c> answer.</summary>
    private const string WithParticipants =
        "select=*,participants:tournament_registration_participants(slot_number,player_id," +
        "player:players(id,first_name,last_name,club,gender_id,category_id,birth_date))";

    private const string ParticipantsOrder = "participants.order=slot_number.asc";

    private readonly HttpClient _http = factory.CreateClient(SupabaseHttpClientName.Name);

    public async Task<IReadOnlyList<TournamentRegistrationDetails>> ListByTournamentAsync(
        Guid tournamentId,
        RegistrationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var filter = status is { } value ? $"&status=eq.{value.ToWireName()}" : string.Empty;
        var rows = await GetRowsAsync(
            $"{WithParticipants}&tournament_id=eq.{tournamentId}{filter}&order=created_at_utc.asc,id.asc&{ParticipantsOrder}",
            cancellationToken);
        return rows.Select(ToDetails).ToList();
    }

    public async Task<TournamentRegistrationDetails?> GetAsync(
        Guid tournamentId,
        TournamentRegistrationId id,
        CancellationToken cancellationToken = default)
    {
        var rows = await GetRowsAsync(
            $"{WithParticipants}&id=eq.{id}&tournament_id=eq.{tournamentId}&{ParticipantsOrder}&limit=1",
            cancellationToken);
        return rows.Count == 0 ? null : ToDetails(rows[0]);
    }

    public Task<TournamentRegistrationDetails> CreateAsync(RegistrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parameters = new CreateParameters(
            request.TournamentId.Value,
            request.Source.ToWireName(),
            request.RequestedStatus.ToWireName(),
            request.PaymentStatus.ToWireName(),
            request.ContactEmail,
            request.Notes,
            request.PublicNameConsent,
            request.Participants.Select(p => new ParticipantParameter(p.SlotNumber, p.PlayerId.Value)).ToList(),
            clock.UtcNow);
        return CallAsync(CreateRpc, parameters, cancellationToken);
    }

    public Task<TournamentRegistrationDetails> TransitionAsync(
        TournamentRegistrationVersion expected,
        RegistrationStatus status,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);

        var parameters = new TransitionParameters(expected.RegistrationId.Value, expected.Revision, status.ToWireName(), clock.UtcNow);
        return CallAsync(TransitionRpc, parameters, cancellationToken);
    }

    public Task<TournamentRegistrationDetails> SetPaymentStatusAsync(
        TournamentRegistrationVersion expected,
        RegistrationPaymentStatus paymentStatus,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);

        var parameters = new PaymentParameters(expected.RegistrationId.Value, expected.Revision, paymentStatus.ToWireName(), clock.UtcNow);
        return CallAsync(PaymentRpc, parameters, cancellationToken);
    }

    public async Task<int> CountAsync(
        Guid tournamentId,
        IReadOnlyCollection<RegistrationStatus> statuses,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        var filter = statuses.Count == 0
            ? string.Empty
            : $"&status=in.({string.Join(",", statuses.Distinct().Select(s => s.ToWireName()))})";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{RestPath}?select=id&tournament_id=eq.{tournamentId}{filter}&limit=1");
        request.Headers.Add("Prefer", "count=exact");
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return SupabaseResponses.ReadTotalCount(response) ?? 0;
    }

    /// <summary>
    /// Calls a registration function (one POST with the named parameters) and maps its document. A refusal becomes
    /// <see cref="PersistenceConflictException"/>; any other failure keeps the <see cref="HttpRequestException"/> path.
    /// </summary>
    private async Task<TournamentRegistrationDetails> CallAsync(string path, object parameters, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(path, parameters, cancellationToken);
        if (await SupabaseResponses.ReadConflictAsync(response, cancellationToken) is { Code: SupabaseResponses.RefusedWriteCode } conflict
            && RegistrationErrors.FromStoreCode(conflict.Message) is { } error)
            throw new PersistenceConflictException(error);

        response.EnsureSuccessStatusCode();
        var row = await response.Content.ReadFromJsonAsync<RegistrationRow>(cancellationToken)
            ?? throw new JsonException($"The function {path} returned no result.");
        return ToDetails(row);
    }

    private async Task<List<RegistrationRow>> GetRowsAsync(string query, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{RestPath}?{query}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<RegistrationRow>>(cancellationToken) ?? [];
    }

    private static TournamentRegistrationDetails ToDetails(RegistrationRow row)
    {
        var participants = (row.Participants ?? []).OrderBy(p => p.SlotNumber).ToList();
        var registration = TournamentRegistration.Reconstitute(
            new TournamentRegistrationId(row.Id),
            new TournamentId(row.TournamentId),
            new OrganizationId(row.OrganizationId),
            Parse<RegistrationStatus>(row.Status, "status", RegistrationWireNames.TryParse),
            Parse<RegistrationSource>(row.Source, "source", RegistrationWireNames.TryParse),
            Parse<RegistrationPaymentStatus>(row.PaymentStatus, "payment_status", RegistrationWireNames.TryParse),
            row.ContactEmail,
            row.Notes,
            row.PublicNameConsent,
            participants.Select(p => new RegistrationParticipant(p.SlotNumber, new PlayerId(p.PlayerId))).ToList(),
            row.Revision,
            row.CreatedAtUtc.UtcDateTime,
            row.UpdatedAtUtc.UtcDateTime);

        return new TournamentRegistrationDetails(
            registration,
            participants.Select(p => new RegisteredParticipant(
                p.SlotNumber,
                new PlayerId(p.PlayerId),
                p.Player.FirstName,
                p.Player.LastName,
                p.Player.Club,
                p.Player.GenderId,
                p.Player.CategoryId,
                p.Player.BirthDate)).ToList());
    }

    private delegate bool Parser<T>(string? name, out T value);

    private static T Parse<T>(string value, string column, Parser<T> parse) =>
        parse(value, out var parsed) ? parsed : throw new JsonException($"Unknown stored {column} '{value}'.");

    private sealed record CreateParameters(
        [property: JsonPropertyName("p_tournament_id")] Guid TournamentId,
        [property: JsonPropertyName("p_source")] string Source,
        [property: JsonPropertyName("p_requested_status")] string RequestedStatus,
        [property: JsonPropertyName("p_payment_status")] string PaymentStatus,
        [property: JsonPropertyName("p_contact_email")] string? ContactEmail,
        [property: JsonPropertyName("p_notes")] string? Notes,
        [property: JsonPropertyName("p_public_name_consent")] bool PublicNameConsent,
        [property: JsonPropertyName("p_participants")] IReadOnlyList<ParticipantParameter> Participants,
        [property: JsonPropertyName("p_now_utc")] DateTime NowUtc);

    private sealed record ParticipantParameter(
        [property: JsonPropertyName("slot_number")] int SlotNumber,
        [property: JsonPropertyName("player_id")] Guid PlayerId);

    private sealed record TransitionParameters(
        [property: JsonPropertyName("p_registration_id")] Guid RegistrationId,
        [property: JsonPropertyName("p_expected_revision")] int ExpectedRevision,
        [property: JsonPropertyName("p_status")] string Status,
        [property: JsonPropertyName("p_now_utc")] DateTime NowUtc);

    private sealed record PaymentParameters(
        [property: JsonPropertyName("p_registration_id")] Guid RegistrationId,
        [property: JsonPropertyName("p_expected_revision")] int ExpectedRevision,
        [property: JsonPropertyName("p_payment_status")] string PaymentStatus,
        [property: JsonPropertyName("p_now_utc")] DateTime NowUtc);

    private sealed record RegistrationRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("tournament_id")] Guid TournamentId,
        [property: JsonPropertyName("organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("payment_status")] string PaymentStatus,
        [property: JsonPropertyName("contact_email")] string? ContactEmail,
        [property: JsonPropertyName("notes")] string? Notes,
        [property: JsonPropertyName("public_name_consent")] bool PublicNameConsent,
        [property: JsonPropertyName("revision")] int Revision,
        [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
        [property: JsonPropertyName("participants")] List<ParticipantRow>? Participants = null);

    private sealed record ParticipantRow(
        [property: JsonPropertyName("slot_number")] int SlotNumber,
        [property: JsonPropertyName("player_id")] Guid PlayerId,
        [property: JsonPropertyName("player")] PlayerSummaryRow Player);

    private sealed record PlayerSummaryRow(
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string LastName,
        [property: JsonPropertyName("club")] string? Club,
        [property: JsonPropertyName("gender_id")] Guid? GenderId,
        [property: JsonPropertyName("category_id")] Guid? CategoryId,
        [property: JsonPropertyName("birth_date")] DateOnly? BirthDate);
}
