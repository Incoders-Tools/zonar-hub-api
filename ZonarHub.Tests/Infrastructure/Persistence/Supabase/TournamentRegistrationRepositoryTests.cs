using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Registrations;
using ZonarHub.Domain.Tournaments;
using ZonarHub.Infrastructure.Persistence.Supabase;
using ZonarHub.Tests.Common;
using static ZonarHub.Tests.Infrastructure.Persistence.Supabase.SupabaseHttpRecorder;

namespace ZonarHub.Tests.Infrastructure.Persistence.Supabase;

/// <summary>
/// PostgREST side of tournament registrations (S4, D1, D2): embedded participants with player summaries, one function
/// call per write with the expected revision, and every PT409 refusal mapped to its <see cref="RegistrationErrors"/> code.
/// </summary>
public class TournamentRegistrationRepositoryTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 30, 0, DateTimeKind.Utc);
    private static readonly Guid Tournament = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Organization = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly TournamentRegistrationId Id = new(Guid.Parse("00000000-0000-0000-0000-0000000000e1"));
    private static readonly PlayerId PlayerA = new(Guid.Parse("00000000-0000-0000-0000-0000000000f1"));
    private static readonly PlayerId PlayerB = new(Guid.Parse("00000000-0000-0000-0000-0000000000f2"));
    private static readonly Guid Gender = Guid.Parse("a1000000-0000-0000-0000-000000000001");

    private const string Select =
        "select=*,participants:tournament_registration_participants(slot_number,player_id," +
        "player:players(id,first_name,last_name,club,gender_id,category_id,birth_date))";

    [Fact]
    public async Task ListByTournament_EmbedsParticipantsInSlotOrder_AndMapsTheRegistration()
    {
        var http = new SupabaseHttpRecorder(_ => Json($"[{RowJson()}]"));

        var registrations = await Repository(http).ListByTournamentAsync(Tournament);

        var request = http.Single();
        Assert.Equal(("GET", "/rest/v1/tournament_registrations"), (request.Method, request.Path));
        Assert.Equal(
            [Select, $"tournament_id=eq.{Tournament}", "order=created_at_utc.asc,id.asc", "participants.order=slot_number.asc"],
            request.Query);
        var details = Assert.Single(registrations);
        var registration = details.Registration;
        Assert.Equal((Id, new TournamentId(Tournament), Organization), (registration.Id, registration.TournamentId, registration.OrganizationId.Value));
        Assert.Equal(RegistrationStatus.Waitlisted, registration.Status);
        Assert.Equal(RegistrationSource.Public, registration.Source);
        Assert.Equal(RegistrationPaymentStatus.NotRequired, registration.PaymentStatus);
        Assert.Equal(("pair@example.com", "notes"), (registration.ContactEmail, registration.Notes));
        Assert.True(registration.PublicNameConsent);
        Assert.Equal(4, registration.Revision);
        Assert.Equal(new TournamentRegistrationVersion(Id, 4), details.Version);
        Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc), registration.CreatedAtUtc);
        Assert.Equal(new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc), registration.UpdatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, registration.UpdatedAtUtc.Kind);
        Assert.Equal([new RegistrationParticipant(1, PlayerA), new RegistrationParticipant(2, PlayerB)], registration.Participants);
        Assert.Equal(
            [new RegisteredParticipant(1, PlayerA, "Ana", "Pérez", "Club Norte", Gender, null, new DateOnly(1990, 5, 1)),
                new RegisteredParticipant(2, PlayerB, "Bea", "Gómez", null, null, null, null)],
            details.Participants);
        Assert.Equal("Ana Pérez", details.Participants[0].DisplayName);
    }

    [Theory]
    [InlineData(RegistrationStatus.Pending, "pending")]
    [InlineData(RegistrationStatus.Confirmed, "confirmed")]
    [InlineData(RegistrationStatus.Rejected, "rejected")]
    [InlineData(RegistrationStatus.Waitlisted, "waitlisted")]
    [InlineData(RegistrationStatus.Cancelled, "cancelled")]
    public async Task ListByTournament_StatusFilter_UsesTheWireName(RegistrationStatus status, string wire)
    {
        var http = new SupabaseHttpRecorder(_ => Json($"[{RowJson(status: wire)}]"));

        var registrations = await Repository(http).ListByTournamentAsync(Tournament, status);

        Assert.Contains($"status=eq.{wire}", http.Single().Query);
        Assert.Equal(status, Assert.Single(registrations).Registration.Status);
    }

    [Fact]
    public async Task Get_IsScopedToTheTournament_AndNoRowIsNull()
    {
        var found = new SupabaseHttpRecorder(_ => Json($"[{RowJson(participants: false)}]"));
        var none = new SupabaseHttpRecorder(_ => Json("null"));

        var details = await Repository(found).GetAsync(Tournament, Id);

        Assert.Equal(
            [Select, $"id=eq.{Id}", $"tournament_id=eq.{Tournament}", "participants.order=slot_number.asc", "limit=1"],
            found.Single().Query);
        Assert.Empty(details!.Participants);
        Assert.Empty(details.Registration.Participants);
        Assert.Null(await Repository(none).GetAsync(Tournament, Id));
    }

    [Fact]
    public async Task Create_IsOneRpcCall_WithWireNamesAndParticipants()
    {
        var http = new SupabaseHttpRecorder(_ => Json(RowJson(status: "pending")));
        var request = RegistrationRequest.Create(
            new TournamentId(Tournament), RegistrationSource.Admin, RegistrationStatus.Confirmed, RegistrationPaymentStatus.Pending,
            "pair@example.com", null, publicNameConsent: false,
            [new RegistrationParticipant(2, PlayerB), new RegistrationParticipant(1, PlayerA)]).Value;

        var created = await Repository(http).CreateAsync(request);

        var sent = http.Single();
        Assert.Equal(("POST", "/rest/v1/rpc/create_tournament_registration"), (sent.Method, sent.Path));
        Assert.Empty(sent.Query);
        var body = JsonNode.Parse(sent.Body!)!.AsObject();
        Assert.Equal(
            ["p_tournament_id", "p_source", "p_requested_status", "p_payment_status", "p_contact_email", "p_notes",
                "p_public_name_consent", "p_participants", "p_now_utc"],
            body.Select(p => p.Key));
        Assert.Equal(Tournament.ToString(), body["p_tournament_id"]!.GetValue<string>());
        Assert.Equal(("admin", "confirmed", "pending"), (body["p_source"]!.GetValue<string>(),
            body["p_requested_status"]!.GetValue<string>(), body["p_payment_status"]!.GetValue<string>()));
        Assert.Equal("pair@example.com", body["p_contact_email"]!.GetValue<string>());
        Assert.Null(body["p_notes"]);
        Assert.False(body["p_public_name_consent"]!.GetValue<bool>());
        Assert.Equal(
            $$"""[{"slot_number":1,"player_id":"{{PlayerA}}"},{"slot_number":2,"player_id":"{{PlayerB}}"}]""",
            body["p_participants"]!.ToJsonString());
        Assert.Equal("2026-11-10T12:30:00Z", body["p_now_utc"]!.GetValue<string>());
        Assert.Equal(RegistrationStatus.Pending, created.Registration.Status);
        Assert.Equal(2, created.Participants.Count);
    }

    [Fact]
    public async Task Create_PublicSourceAndEveryPaymentStatus_UseTheirWireNames()
    {
        foreach (var (payment, wire) in new[]
                 {
                     (RegistrationPaymentStatus.NotRequired, "not_required"), (RegistrationPaymentStatus.Pending, "pending"),
                     (RegistrationPaymentStatus.Paid, "paid"), (RegistrationPaymentStatus.Refunded, "refunded"),
                     (RegistrationPaymentStatus.Failed, "failed"),
                 })
        {
            var http = new SupabaseHttpRecorder(_ => Json(RowJson(payment: wire)));
            var request = RegistrationRequest.Create(
                new TournamentId(Tournament), RegistrationSource.Public, RegistrationStatus.Pending, payment,
                "pair@example.com", "n", publicNameConsent: true, [new RegistrationParticipant(1, PlayerA)]).Value;

            var created = await Repository(http).CreateAsync(request);

            var body = JsonNode.Parse(http.Single().Body!)!.AsObject();
            Assert.Equal(("public", wire), (body["p_source"]!.GetValue<string>(), body["p_payment_status"]!.GetValue<string>()));
            Assert.Equal(payment, created.Registration.PaymentStatus);
        }
    }

    [Fact]
    public async Task Transition_SendsTheExpectedRevisionAndTarget()
    {
        var http = new SupabaseHttpRecorder(_ => Json(RowJson(status: "confirmed", revision: 5)));

        var moved = await Repository(http).TransitionAsync(new TournamentRegistrationVersion(Id, 4), RegistrationStatus.Confirmed);

        var sent = http.Single();
        Assert.Equal("/rest/v1/rpc/transition_tournament_registration", sent.Path);
        Assert.Equal(
            $$"""{"p_registration_id":"{{Id}}","p_expected_revision":4,"p_status":"confirmed","p_now_utc":"2026-11-10T12:30:00Z"}""",
            sent.Body);
        Assert.Equal((RegistrationStatus.Confirmed, 5), (moved.Registration.Status, moved.Registration.Revision));
    }

    [Fact]
    public async Task SetPaymentStatus_SendsTheExpectedRevisionAndStatus()
    {
        var http = new SupabaseHttpRecorder(_ => Json(RowJson(payment: "paid", revision: 5)));

        var paid = await Repository(http).SetPaymentStatusAsync(new TournamentRegistrationVersion(Id, 4), RegistrationPaymentStatus.Paid);

        var sent = http.Single();
        Assert.Equal("/rest/v1/rpc/set_tournament_registration_payment_status", sent.Path);
        Assert.Equal(
            $$"""{"p_registration_id":"{{Id}}","p_expected_revision":4,"p_payment_status":"paid","p_now_utc":"2026-11-10T12:30:00Z"}""",
            sent.Body);
        Assert.Equal(RegistrationPaymentStatus.Paid, paid.Registration.PaymentStatus);
    }

    [Theory]
    [InlineData("registrations.capacity_reached")]
    [InlineData("registrations.player_already_registered")]
    [InlineData("registrations.concurrent_modification")]
    [InlineData("registrations.invalid_transition")]
    [InlineData("registrations.tournament_not_found")]
    [InlineData("registrations.player_not_found")]
    public async Task Writes_RefusedByTheStore_AreConflictsWithTheSameCode(string code)
    {
        var http = new SupabaseHttpRecorder(_ => Problem(HttpStatusCode.Conflict, "PT409", code));
        var repository = Repository(http);

        foreach (var write in Writes(repository))
        {
            var error = await Assert.ThrowsAsync<PersistenceConflictException>(write);
            Assert.Same(RegistrationErrors.FromStoreCode(code), error.Error);
        }
    }

    [Fact]
    public async Task Writes_OtherFailures_KeepTheHttpRequestExceptionPath()
    {
        foreach (var respond in new Func<HttpResponseMessage>[]
                 {
                     () => Problem(HttpStatusCode.Conflict, "PT409", "registrations.something_else"),
                     () => Problem(HttpStatusCode.Conflict, "23505", "registrations.capacity_reached"),
                     () => Problem(HttpStatusCode.BadRequest, "PT409", "registrations.capacity_reached"),
                     () => new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("conflict") },
                     () => new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("null") },
                     () => Problem(HttpStatusCode.BadRequest, "P0001", "invalid registration create arguments"),
                 })
        {
            var repository = Repository(new SupabaseHttpRecorder(_ => respond()));
            foreach (var write in Writes(repository))
                await Assert.ThrowsAsync<HttpRequestException>(write);
        }
    }

    [Fact]
    public async Task Writes_NullFunctionResult_IsAJsonException()
    {
        var repository = Repository(new SupabaseHttpRecorder(_ => Json("null")));

        foreach (var write in Writes(repository))
            await Assert.ThrowsAsync<JsonException>(write);
    }

    [Fact]
    public async Task Reads_StoredValuesOutsideTheContract_AreRejected()
    {
        foreach (var row in new[] { RowJson(status: "archived"), RowJson(payment: "free"), RowJson().Replace("\"public\"", "\"kiosk\"") })
        {
            var repository = Repository(new SupabaseHttpRecorder(_ => Json($"[{row}]")));
            await Assert.ThrowsAsync<JsonException>(() => repository.ListByTournamentAsync(Tournament));
        }
    }

    [Fact]
    public async Task Reads_StoreFailures_ThrowHttpRequestException()
    {
        var repository = Repository(new SupabaseHttpRecorder(_ => Problem(HttpStatusCode.InternalServerError, "XX000", "boom")));

        await Assert.ThrowsAsync<HttpRequestException>(() => repository.ListByTournamentAsync(Tournament));
        await Assert.ThrowsAsync<HttpRequestException>(() => repository.GetAsync(Tournament, Id));
        await Assert.ThrowsAsync<HttpRequestException>(() => repository.CountAsync(Tournament, []));
    }

    [Fact]
    public async Task Count_FiltersByStatuses_AndReadsTheExactCount()
    {
        var http = new SupabaseHttpRecorder(_ => Counted("[]", "0-0/7"));

        var count = await Repository(http).CountAsync(Tournament, [RegistrationStatus.Confirmed, RegistrationStatus.Pending]);

        var request = http.Single();
        Assert.Equal(("GET", "/rest/v1/tournament_registrations", "count=exact"), (request.Method, request.Path, request.Prefer));
        Assert.Equal(["select=id", $"tournament_id=eq.{Tournament}", "status=in.(confirmed,pending)", "limit=1"], request.Query);
        Assert.Equal(7, count);
    }

    [Fact]
    public async Task Count_WithoutStatuses_CountsAll_AndAMissingTotalIsZero()
    {
        var all = new SupabaseHttpRecorder(_ => Counted("[]", "*/12"));
        var missing = new SupabaseHttpRecorder(_ => Json("[]"));
        var unknown = new SupabaseHttpRecorder(_ => Counted("[]", "0-0/*"));

        Assert.Equal(12, await Repository(all).CountAsync(Tournament, []));
        Assert.Equal(["select=id", $"tournament_id=eq.{Tournament}", "limit=1"], all.Single().Query);
        Assert.Equal(0, await Repository(missing).CountAsync(Tournament, [RegistrationStatus.Cancelled]));
        Assert.Equal(0, await Repository(unknown).CountAsync(Tournament, [RegistrationStatus.Cancelled]));
    }

    private static TournamentRegistrationRepository Repository(SupabaseHttpRecorder http) => new(http.Factory, new TestClock(Now));

    private static IEnumerable<Func<Task>> Writes(TournamentRegistrationRepository repository)
    {
        var request = RegistrationRequest.Create(
            new TournamentId(Tournament), RegistrationSource.Admin, RegistrationStatus.Pending, RegistrationPaymentStatus.NotRequired,
            null, null, false, [new RegistrationParticipant(1, PlayerA)]).Value;
        var version = new TournamentRegistrationVersion(Id, 1);
        yield return () => repository.CreateAsync(request);
        yield return () => repository.TransitionAsync(version, RegistrationStatus.Cancelled);
        yield return () => repository.SetPaymentStatusAsync(version, RegistrationPaymentStatus.Paid);
    }

    private static string RowJson(string status = "waitlisted", string payment = "not_required", int revision = 4, bool participants = true)
    {
        var row = $$"""
            {"id":"{{Id}}","tournament_id":"{{Tournament}}","organization_id":"{{Organization}}","status":"{{status}}",
             "source":"public","payment_status":"{{payment}}","contact_email":"pair@example.com","notes":"notes",
             "public_name_consent":true,"is_active":true,"revision":{{revision}},
             "created_at_utc":"2026-11-10T09:00:00+00:00","updated_at_utc":"2026-11-10T07:00:00-03:00"
            """;
        var embedded = $$"""
            ,"participants":[
              {"slot_number":2,"player_id":"{{PlayerB}}","player":{"id":"{{PlayerB}}","first_name":"Bea","last_name":"Gómez",
                "club":null,"gender_id":null,"category_id":null,"birth_date":null} },
              {"slot_number":1,"player_id":"{{PlayerA}}","player":{"id":"{{PlayerA}}","first_name":"Ana","last_name":"Pérez",
                "club":"Club Norte","gender_id":"{{Gender}}","category_id":null,"birth_date":"1990-05-01"} }]
            """;
        return row + (participants ? embedded : string.Empty) + "}";
    }
}
