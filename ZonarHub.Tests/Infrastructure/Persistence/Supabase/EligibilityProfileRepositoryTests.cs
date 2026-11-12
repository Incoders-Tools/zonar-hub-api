using System.Net;
using System.Text.Json.Nodes;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Registrations;
using ZonarHub.Infrastructure.Persistence.Supabase;
using static ZonarHub.Tests.Infrastructure.Persistence.Supabase.SupabaseHttpRecorder;

namespace ZonarHub.Tests.Infrastructure.Persistence.Supabase;

/// <summary>PostgREST side of eligibility profiles (D1): embedded slots, one atomic save function, restricted delete.</summary>
public class EligibilityProfileRepositoryTests
{
    private static readonly OrganizationId Organization = new(Guid.Parse("00000000-0000-0000-0000-0000000000b1"));
    private static readonly EligibilityProfileId Id = new(Guid.Parse("00000000-0000-0000-0000-0000000000e1"));
    private static readonly Guid Female = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid Fourth = Guid.Parse("b1000000-0000-0000-0000-000000000004");

    [Fact]
    public async Task List_EmbedsSlotsInOrder_ForTheOrganization()
    {
        var http = new SupabaseHttpRecorder(_ => Json($"[{RowJson()}]"));

        var profiles = await new EligibilityProfileRepository(http.Factory).ListByOrganizationAsync(Organization);

        var request = http.Single();
        Assert.Equal(("GET", "/rest/v1/tournament_eligibility_profiles"), (request.Method, request.Path));
        Assert.Equal(
            ["select=*,slots:tournament_eligibility_profile_slots(*)", $"organization_id=eq.{Organization}",
                "order=name.asc,id.asc", "slots.order=slot_number.asc"],
            request.Query);
        var profile = Assert.Single(profiles);
        Assert.Equal((Id, Organization, "Damas 4ta", "Pairs"), (profile.Id, profile.OrganizationId, profile.Name, profile.Description));
        Assert.True(profile.IsActive);
        Assert.Equal(
            [new EligibilitySlot(1, Female, Fourth, 18, 45, "Player 1", true), new EligibilitySlot(2, null, null, null, null, null, false)],
            profile.Slots);
        Assert.Equal(new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc), profile.CreatedAtUtc);
        Assert.Equal(new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc), profile.UpdatedAtUtc);
    }

    [Fact]
    public async Task GetById_AddsTheIdFilter_AndNoRowIsNull()
    {
        var found = new SupabaseHttpRecorder(_ => Json($"[{RowJson(slots: false)}]"));
        var none = new SupabaseHttpRecorder(_ => Json("[]"));

        var profile = await new EligibilityProfileRepository(found.Factory).GetByIdAsync(Organization, Id);

        Assert.Equal(
            ["select=*,slots:tournament_eligibility_profile_slots(*)", $"organization_id=eq.{Organization}", $"id=eq.{Id}",
                "slots.order=slot_number.asc", "limit=1"],
            found.Single().Query);
        Assert.Empty(profile!.Slots);
        Assert.Null(await new EligibilityProfileRepository(none.Factory).GetByIdAsync(Organization, Id));
    }

    [Fact]
    public async Task Save_IsOneRpcCall_WithEverySlot_AndMapsTheStoredProfile()
    {
        var http = new SupabaseHttpRecorder(_ => Json(RowJson()));
        var profile = EligibilityProfile.Reconstitute(Id, Organization, "Damas 4ta", "Pairs", true,
            [new EligibilitySlot(1, Female, Fourth, 18, 45, "Player 1", true), new EligibilitySlot(2, null, null, null, null, null, false)],
            new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc));

        var saved = await new EligibilityProfileRepository(http.Factory).SaveAsync(profile);

        var request = http.Single();
        Assert.Equal(("POST", "/rest/v1/rpc/save_tournament_eligibility_profile"), (request.Method, request.Path));
        var body = JsonNode.Parse(request.Body!)!.AsObject();
        Assert.Equal(
            ["p_profile_id", "p_organization_id", "p_name", "p_description", "p_is_active", "p_slots", "p_now_utc"],
            body.Select(p => p.Key));
        Assert.Equal(Id.ToString(), body["p_profile_id"]!.GetValue<string>());
        Assert.Equal("2026-11-02T09:00:00Z", body["p_now_utc"]!.GetValue<string>());
        var slots = body["p_slots"]!.AsArray();
        Assert.Equal(
            ["slot_number", "gender_id", "category_id", "min_age", "max_age", "label", "required"],
            slots[0]!.AsObject().Select(p => p.Key));
        Assert.Equal(Female.ToString(), slots[0]!["gender_id"]!.GetValue<string>());
        Assert.Null(slots[1]!["gender_id"]);
        Assert.False(slots[1]!["required"]!.GetValue<bool>());
        Assert.Equal(2, saved.Slots.Count);
        Assert.Equal(new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc), saved.CreatedAtUtc);
    }

    [Fact]
    public async Task Save_ProfileOfAnotherOrganization_IsNotFound_AndTakenNameIsNameTaken()
    {
        var foreign = new SupabaseHttpRecorder(_ => Problem(HttpStatusCode.Conflict, "PT409", "eligibility_profiles.not_found"));
        var taken = new SupabaseHttpRecorder(_ => Problem(HttpStatusCode.Conflict, "23505",
            "duplicate key value violates unique constraint \"ux_tournament_eligibility_profiles_organization_name\""));
        var profile = EligibilityProfile.Reconstitute(Id, Organization, "P", null, true, [new EligibilitySlot(1, null, null, null, null, null, true)],
            DateTime.UtcNow, DateTime.UtcNow);

        Assert.Equal("eligibility_profiles.not_found",
            (await Assert.ThrowsAsync<PersistenceConflictException>(() => new EligibilityProfileRepository(foreign.Factory).SaveAsync(profile))).Error.Code);
        Assert.Equal("eligibility_profiles.name_taken",
            (await Assert.ThrowsAsync<PersistenceConflictException>(() => new EligibilityProfileRepository(taken.Factory).SaveAsync(profile))).Error.Code);
    }

    [Fact]
    public async Task Save_OtherFailures_KeepTheHttpRequestExceptionPath()
    {
        var unknown = new SupabaseHttpRecorder(_ => Problem(HttpStatusCode.Conflict, "PT409", "eligibility_profiles.other"));
        var check = new SupabaseHttpRecorder(_ => Problem(HttpStatusCode.BadRequest, "23514", "violates check constraint"));
        var profile = EligibilityProfile.Reconstitute(Id, Organization, "P", null, true, [], DateTime.UtcNow, DateTime.UtcNow);

        await Assert.ThrowsAsync<HttpRequestException>(() => new EligibilityProfileRepository(unknown.Factory).SaveAsync(profile));
        await Assert.ThrowsAsync<HttpRequestException>(() => new EligibilityProfileRepository(check.Factory).SaveAsync(profile));
    }

    [Fact]
    public async Task Save_NullFunctionResult_IsAJsonException()
    {
        var http = new SupabaseHttpRecorder(_ => Json("null"));
        var profile = EligibilityProfile.Reconstitute(Id, Organization, "P", null, true, [], DateTime.UtcNow, DateTime.UtcNow);

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => new EligibilityProfileRepository(http.Factory).SaveAsync(profile));
    }

    [Fact]
    public async Task Delete_IsScopedToTheOrganization_AndReportsWhetherARowWent()
    {
        var deleted = new SupabaseHttpRecorder(_ => Json($$"""[{"id":"{{Id}}"}]"""));
        var missing = new SupabaseHttpRecorder(_ => Json("[]"));

        Assert.True(await new EligibilityProfileRepository(deleted.Factory).DeleteAsync(Organization, Id));
        Assert.False(await new EligibilityProfileRepository(missing.Factory).DeleteAsync(Organization, Id));

        var request = deleted.Single();
        Assert.Equal(("DELETE", "/rest/v1/tournament_eligibility_profiles", "return=representation"), (request.Method, request.Path, request.Prefer));
        Assert.Equal([$"id=eq.{Id}", $"organization_id=eq.{Organization}", "select=id"], request.Query);
    }

    [Fact]
    public async Task Delete_ReferencedByATournament_IsInUse()
    {
        var http = new SupabaseHttpRecorder(_ => Problem(HttpStatusCode.Conflict, "23503",
            "update or delete on table \"tournament_eligibility_profiles\" violates foreign key constraint \"fk_tournaments_eligibility_profile\" on table \"tournaments\""));
        var other = new SupabaseHttpRecorder(_ => Problem(HttpStatusCode.Conflict, "23503", "violates foreign key constraint \"fk_other\""));

        var error = await Assert.ThrowsAsync<PersistenceConflictException>(() => new EligibilityProfileRepository(http.Factory).DeleteAsync(Organization, Id));

        Assert.Equal("eligibility_profiles.in_use", error.Error.Code);
        await Assert.ThrowsAsync<HttpRequestException>(() => new EligibilityProfileRepository(other.Factory).DeleteAsync(Organization, Id));
    }

    [Fact]
    public async Task GetTournamentProfileId_ReadsTheTournamentsColumn()
    {
        var tournament = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
        var assigned = new SupabaseHttpRecorder(_ => Json($$"""[{"eligibility_profile_id":"{{Id}}"}]"""));
        var unassigned = new SupabaseHttpRecorder(_ => Json("""[{"eligibility_profile_id":null}]"""));
        var missing = new SupabaseHttpRecorder(_ => Json("[]"));

        var id = await new EligibilityProfileRepository(assigned.Factory).GetTournamentProfileIdAsync(tournament);

        var request = assigned.Single();
        Assert.Equal(("GET", "/rest/v1/tournaments"), (request.Method, request.Path));
        Assert.Equal(["select=eligibility_profile_id", $"id=eq.{tournament}", "limit=1"], request.Query);
        Assert.Equal(Id, id);
        Assert.Null(await new EligibilityProfileRepository(unassigned.Factory).GetTournamentProfileIdAsync(tournament));
        Assert.Null(await new EligibilityProfileRepository(missing.Factory).GetTournamentProfileIdAsync(tournament));
    }

    [Fact]
    public async Task SetTournamentProfileId_PatchesOnlyTheTournamentsColumn_AndNullClearsIt()
    {
        var tournament = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
        var assigned = new SupabaseHttpRecorder(_ => Json($$"""[{"eligibility_profile_id":"{{Id}}"}]"""));
        var cleared = new SupabaseHttpRecorder(_ => Json("""[{"eligibility_profile_id":null}]"""));
        var missing = new SupabaseHttpRecorder(_ => Json("[]"));

        Assert.True(await new EligibilityProfileRepository(assigned.Factory).SetTournamentProfileIdAsync(tournament, Id));
        Assert.True(await new EligibilityProfileRepository(cleared.Factory).SetTournamentProfileIdAsync(tournament, null));
        Assert.False(await new EligibilityProfileRepository(missing.Factory).SetTournamentProfileIdAsync(tournament, Id));

        var request = assigned.Single();
        Assert.Equal(("PATCH", "/rest/v1/tournaments", "return=representation"), (request.Method, request.Path, request.Prefer));
        Assert.Equal([$"id=eq.{tournament}", "select=eligibility_profile_id"], request.Query);
        var body = JsonNode.Parse(request.Body!)!.AsObject();
        Assert.Equal(["eligibility_profile_id"], body.Select(p => p.Key));
        Assert.Equal(Id.ToString(), body["eligibility_profile_id"]!.GetValue<string>());
        var clear = JsonNode.Parse(cleared.Single().Body!)!.AsObject();
        Assert.Equal(["eligibility_profile_id"], clear.Select(p => p.Key));
        Assert.Null(clear["eligibility_profile_id"]);
    }

    [Fact]
    public async Task SetTournamentProfileId_MissingProfile_IsNotFound_OtherFailuresThrow()
    {
        var tournament = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
        var deleted = new SupabaseHttpRecorder(_ => Problem(HttpStatusCode.Conflict, "23503",
            "insert or update on table \"tournaments\" violates foreign key constraint \"fk_tournaments_eligibility_profile\""));
        var other = new SupabaseHttpRecorder(_ => Problem(HttpStatusCode.Conflict, "23503", "violates foreign key constraint \"fk_other\""));

        var error = await Assert.ThrowsAsync<PersistenceConflictException>(
            () => new EligibilityProfileRepository(deleted.Factory).SetTournamentProfileIdAsync(tournament, Id));

        Assert.Equal("eligibility_profiles.not_found", error.Error.Code);
        await Assert.ThrowsAsync<HttpRequestException>(() => new EligibilityProfileRepository(other.Factory).SetTournamentProfileIdAsync(tournament, Id));
    }

    private static string RowJson(bool slots = true)
    {
        var row = $$"""
            {"id":"{{Id}}","organization_id":"{{Organization}}","name":"Damas 4ta","description":"Pairs","is_active":true,
             "created_at_utc":"2026-11-01T08:00:00+00:00","updated_at_utc":"2026-11-02T09:00:00+00:00"
            """;
        var embedded = $$"""
            ,"slots":[
              {"slot_number":2,"gender_id":null,"category_id":null,"min_age":null,"max_age":null,"label":null,"required":false},
              {"slot_number":1,"gender_id":"{{Female}}","category_id":"{{Fourth}}","min_age":18,"max_age":45,"label":"Player 1","required":true}]
            """;
        return row + (slots ? embedded : string.Empty) + "}";
    }
}
