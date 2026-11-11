using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ZonarHub.ApiService.Endpoints.Public;
using ZonarHub.ApiService.Security;
using ZonarHub.Application.Features.PublicRegistrations;
using ZonarHub.Application.Features.PublicTournaments;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Registrations;
using ZonarHub.Tests.Application.PublicRegistrations;
using static ZonarHub.Tests.Api.Registrations.AdminPlayersEndpointTests;
using static ZonarHub.Tests.Application.PublicRegistrations.PublicRegistrationsTestHarness;
using static ZonarHub.Tests.Application.Registrations.RegistrationsTestHarness;
using Endpoints = ZonarHub.ApiService.Endpoints.Public.PublicRegistrationsEndpointsExtensions;

namespace ZonarHub.Tests.Api.PublicRegistrations;

/// <summary>
/// Anonymous public registration by tournament slug (S2, S4, D2, D3, D4, L8) through the endpoints and the real handlers:
/// showcase visibility, registration window, email code (scoped, hashed, expiring, single use, limited attempts and
/// resend cooldown), player reuse without overwriting, capacity, eligibility and the public confirmed pairs.
/// </summary>
public class PublicRegistrationsEndpointTests
{
    private const string Contact = "contact@club.test";

    public static TheoryData<string> Operations => new() { "info", "code", "submit", "confirmedPairs" };

    // ---------- Visibility ----------

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task EveryEndpoint_TournamentOutsideTheShowcase_IsNotFoundWithoutWriting(string operation)
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync(slug: "hidden-cup-abc123", isPublic: false);
        var before = h.StoreSnapshot();

        foreach (var slug in new[] { "hidden-cup-abc123", "unknown-cup-abc123", "NOT A SLUG" })
        {
            var result = await InvokeAsync(operation, h, slug, "123456");

            AssertProblem(result, StatusCodes.Status404NotFound, PublicTournamentErrors.NotFound.Code);
        }

        Assert.Equal(before, h.StoreSnapshot());
        Assert.Empty(h.Templates.Composed);
    }

    // ---------- Info ----------

    [Fact]
    public async Task Info_OpenTournament_ReturnsWindowPlacesFeeSlotsAndConsentKey()
    {
        var h = new PublicRegistrationsTestHarness();
        var tournament = await h.TournamentAsync(maxPairs: 3, fee: 20000m, genderId: Female, categoryId: Fourth);
        h.R.SeedRegistration(tournament, RegistrationStatus.Confirmed);
        h.R.SeedRegistration(tournament, RegistrationStatus.Pending);
        h.R.SeedRegistration(tournament, RegistrationStatus.Waitlisted);
        h.R.SeedRegistration(tournament, RegistrationStatus.Rejected);
        var context = new DefaultHttpContext();

        var info = Assert.IsType<Ok<PublicRegistrationInfoResponse>>(
            await Endpoints.GetInfoAsync(Slug, context, h.Sender, CancellationToken.None)).Value!;

        Assert.Equal((Slug, "open", true, Opens, Closes), (info.Slug, info.State, info.IsOpen, info.OpensOn, info.ClosesOn));
        Assert.Equal(new PublicRegistrationCapacityResponse(3, 1, false), info.Capacity);
        Assert.Equal(new PublicRegistrationFeeResponse(20000m, true), info.Fee);
        Assert.Equal(
            [new PublicRegistrationSlotResponse(1, null, true, Female, Fourth, null, null), new PublicRegistrationSlotResponse(2, null, true, Female, Fourth, null, null)],
            info.Slots);
        Assert.Equal(RegistrationEligibility.CategoryTolerance, info.CategoryTolerance);
        Assert.Equal("public_registrations.consent.public_name", info.ConsentTextKey);
        Assert.Equal("public, max-age=15", context.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData("2026-11-12", "2026-11-20", "notStarted")]
    [InlineData("2026-11-01", "2026-11-10", "ended")]
    public async Task Info_OutsideTheWindow_IsClosed(string opens, string closes, string state)
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync(opens: Day(opens), closes: Day(closes));

        var info = Assert.IsType<Ok<PublicRegistrationInfoResponse>>(
            await Endpoints.GetInfoAsync(Slug, new DefaultHttpContext(), h.Sender, CancellationToken.None)).Value!;

        Assert.Equal((state, false), (info.State, info.IsOpen));
    }

    [Fact]
    public async Task Info_PlacesTakenByConfirmedAndPending_IsWaitlistOnlyWithoutLimitOrFeeOtherwise()
    {
        var h = new PublicRegistrationsTestHarness();
        var full = await h.TournamentAsync(maxPairs: 2);
        h.R.SeedRegistration(full, RegistrationStatus.Confirmed);
        h.R.SeedRegistration(full, RegistrationStatus.Pending);
        await h.TournamentAsync(slug: "free-cup-abc123", window: false);

        var fullInfo = Assert.IsType<Ok<PublicRegistrationInfoResponse>>(
            await Endpoints.GetInfoAsync(Slug, new DefaultHttpContext(), h.Sender, CancellationToken.None)).Value!;
        var freeInfo = Assert.IsType<Ok<PublicRegistrationInfoResponse>>(
            await Endpoints.GetInfoAsync("free-cup-abc123", new DefaultHttpContext(), h.Sender, CancellationToken.None)).Value!;

        Assert.Equal(new PublicRegistrationCapacityResponse(2, 0, true), fullInfo.Capacity);
        Assert.Equal(new PublicRegistrationCapacityResponse(null, null, false), freeInfo.Capacity);
        Assert.Equal(new PublicRegistrationFeeResponse(null, false), freeInfo.Fee);
        Assert.Equal(("open", true, (DateOnly?)null, (DateOnly?)null), (freeInfo.State, freeInfo.IsOpen, freeInfo.OpensOn, freeInfo.ClosesOn));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Info_Profile_ActiveSlotsApply_InactiveFallsBackToTheTournamentsRules(bool active)
    {
        var h = new PublicRegistrationsTestHarness();
        var tournament = await h.TournamentAsync(genderId: Female);
        SeedProfile(h, tournament, active, new EligibilitySlot(1, Male, Third, 18, 40, "Drive", true), new EligibilitySlot(2, null, null, null, null, null, false));

        var info = Assert.IsType<Ok<PublicRegistrationInfoResponse>>(
            await Endpoints.GetInfoAsync(Slug, new DefaultHttpContext(), h.Sender, CancellationToken.None)).Value!;

        Assert.Equal(
            active
                ? [new PublicRegistrationSlotResponse(1, "Drive", true, Male, Third, 18, 40), new PublicRegistrationSlotResponse(2, null, false, null, null, null, null)]
                : [new PublicRegistrationSlotResponse(1, null, true, Female, null, null, null), new PublicRegistrationSlotResponse(2, null, true, Female, null, null, null)],
            info.Slots);
    }

    // ---------- Email code ----------

    [Fact]
    public async Task SendCode_SendsASixDigitCode_AndStoresOnlyItsHashScopedToTheTournamentAndEmail()
    {
        var h = new PublicRegistrationsTestHarness();
        var tournament = await h.TournamentAsync();

        var result = await Endpoints.SendCodeAsync(Slug, new PublicRegistrationCodeBody("  Contact@Club.TEST "), h.Sender, CancellationToken.None);

        var accepted = Assert.IsType<Accepted<PublicRegistrationCodeResponse>>(result);
        Assert.Equal(new PublicRegistrationCodeResponse(900, 60), accepted.Value);
        var composed = Assert.Single(h.Templates.Composed);
        Assert.Equal((Contact, "public_registrations.verification_code"), (composed.To, composed.TemplateKey));
        var code = composed.Variables!["code"];
        Assert.Matches("^[0-9]{6}$", code);
        Assert.Equal(("15", "Open A"), (composed.Variables["expires_minutes"], composed.Variables["tournament_name"]));
        var sent = Assert.Single(h.Email.Sent);
        Assert.Equal(Contact, sent.To);
        Assert.Contains(code, sent.HtmlBody, StringComparison.Ordinal);

        var (key, ttl) = Assert.Single(h.Cache.Sets);
        Assert.Equal($"public-registration-code:{tournament.Id:N}:{Contact}", key);
        Assert.Equal(TimeSpan.FromMinutes(15), ttl);
        Assert.DoesNotContain(code, JsonSerializer.Serialize(h.Cache.Entries[key]), StringComparison.Ordinal);
        Assert.DoesNotContain(h.Cache.Entries.Keys, k => k.StartsWith("verify:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, "registrations.contact_email_required")]
    [InlineData("not-an-email", "registrations.invalid_contact_email")]
    public async Task SendCode_InvalidEmail_IsRefusedWithoutSending(string? email, string code)
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync();

        var result = await Endpoints.SendCodeAsync(Slug, new PublicRegistrationCodeBody(email), h.Sender, CancellationToken.None);

        AssertProblem(result, StatusCodes.Status400BadRequest, code);
        Assert.Empty(h.Email.Sent);
        Assert.Empty(h.Cache.Entries);
    }

    [Fact]
    public async Task SendCode_ClosedWindow_IsConflictWithoutSending()
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync(closes: new DateOnly(2026, 11, 10));

        var result = await Endpoints.SendCodeAsync(Slug, new PublicRegistrationCodeBody(Contact), h.Sender, CancellationToken.None);

        AssertProblem(result, StatusCodes.Status409Conflict, "public_registrations.closed");
        Assert.Empty(h.Email.Sent);
        Assert.Empty(h.Cache.Entries);
    }

    [Fact]
    public async Task SendCode_AgainWithinTheCooldown_IsTooManyRequestsWithoutSending_AndAfterItReplacesTheCode()
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync();
        var first = await h.CodeAsync();
        h.Clock.UtcNow = Now.AddSeconds(59);
        var before = h.StoreSnapshot();

        var again = await Endpoints.SendCodeAsync(Slug, new PublicRegistrationCodeBody(Contact), h.Sender, CancellationToken.None);

        AssertProblem(again, StatusCodes.Status429TooManyRequests, "public_registrations.code_cooldown");
        Assert.Equal(before, h.StoreSnapshot());
        Assert.Single(h.Email.Sent);

        h.Clock.UtcNow = Now.AddSeconds(60);
        var second = await h.CodeAsync();
        Assert.Equal(2, h.Email.Sent.Count);
        if (second != first)
            AssertProblem(await SubmitAsync(h, first), StatusCodes.Status400BadRequest, "public_registrations.invalid_code");
        Assert.IsType<Created<PublicRegistrationSubmittedResponse>>(await SubmitAsync(h, second));
    }

    [Fact]
    public async Task SendCode_IsLimitedPerAddress_NotAcrossAddresses()
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync();
        await h.CodeAsync("one@club.test");

        Assert.IsType<Accepted<PublicRegistrationCodeResponse>>(
            await Endpoints.SendCodeAsync(Slug, new PublicRegistrationCodeBody("two@club.test"), h.Sender, CancellationToken.None));
        Assert.Equal(["one@club.test", "two@club.test"], h.Email.Sent.Select(m => m.To));
    }

    // ---------- Submit: code ----------

    [Fact]
    public async Task Submit_WrongCode_IsRefusedWithoutWriting_AndTheRightCodeStillWorks()
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync();
        var code = await h.CodeAsync();
        var wrong = code == "000000" ? "111111" : "000000";

        foreach (var attempt in new[] { wrong, "", "12345" })
            AssertProblem(await SubmitAsync(h, attempt), StatusCodes.Status400BadRequest, "public_registrations.invalid_code");

        Assert.Empty(h.R.Players.Writes);
        Assert.Empty(h.R.Registrations.Writes);
        Assert.Single(h.Email.Sent);
        Assert.IsType<Created<PublicRegistrationSubmittedResponse>>(await SubmitAsync(h, code));
    }

    [Fact]
    public async Task Submit_AfterTheMaximumWrongAttempts_TheCodeIsGone()
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync();
        var code = await h.CodeAsync();
        var wrong = code == "000000" ? "111111" : "000000";

        for (var attempt = 0; attempt < 5; attempt++)
            AssertProblem(await SubmitAsync(h, wrong), StatusCodes.Status400BadRequest, "public_registrations.invalid_code");

        Assert.Empty(h.Cache.Entries);
        AssertProblem(await SubmitAsync(h, code), StatusCodes.Status400BadRequest, "public_registrations.invalid_code");
        Assert.Empty(h.R.Players.Writes);
        Assert.Empty(h.R.Registrations.Writes);
    }

    [Fact]
    public async Task Submit_ExpiredCode_IsRefusedWithoutWriting()
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync();
        var code = await h.CodeAsync();
        h.Clock.UtcNow = Now.AddMinutes(15);

        AssertProblem(await SubmitAsync(h, code), StatusCodes.Status400BadRequest, "public_registrations.invalid_code");
        Assert.Empty(h.R.Players.Writes);
        Assert.Empty(h.R.Registrations.Writes);
        Assert.Empty(h.Cache.Entries);
    }

    [Fact]
    public async Task Submit_ReusedCode_IsRefused()
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync();
        var code = await h.CodeAsync();
        Assert.IsType<Created<PublicRegistrationSubmittedResponse>>(await SubmitAsync(h, code));
        Assert.Empty(h.Cache.Entries);
        var writes = (h.R.Players.Writes.Count, h.R.Registrations.Writes.Count);

        var again = await SubmitAsync(h, code, Participant(1, "Eva", "Sol", "eva@club.test"), Participant(2, "Flor", "Luz", "flor@club.test"));

        AssertProblem(again, StatusCodes.Status400BadRequest, "public_registrations.invalid_code");
        Assert.Equal(writes, (h.R.Players.Writes.Count, h.R.Registrations.Writes.Count));
    }

    [Fact]
    public async Task Submit_TwoConcurrentSubmissionsWithOneCode_RegisterOnlyOnce()
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync();
        var code = await h.CodeAsync();
        var release = new TaskCompletionSource();
        h.R.Registrations.BeforeNextList = () => release.Task;

        var first = SubmitAsync(h, code);
        var second = SubmitAsync(h, code, Participant(1, "Eva", "Sol", "eva@club.test"), Participant(2, "Flor", "Luz", "flor@club.test"));
        await Task.Yield();
        release.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.IsType<Created<PublicRegistrationSubmittedResponse>>(results[0]);
        AssertProblem(results[1], StatusCodes.Status400BadRequest, "public_registrations.invalid_code");
        Assert.Single(h.R.Registrations.CreatedRequests);
        Assert.Equal(2, h.R.Players.Writes.Count);
        Assert.Empty(h.Cache.Entries);
    }

    [Fact]
    public async Task Submit_CodeOfAnotherEmailOrTournament_OrTheSignUpCode_IsRefused()
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync();
        await h.TournamentAsync(slug: "other-cup-abc123");
        var otherEmail = await h.CodeAsync("other@club.test");
        var otherTournament = await h.CodeAsync(Contact, "other-cup-abc123");
        await h.Cache.SetAsync($"verify:{Contact}", "654321");

        AssertProblem(await SubmitAsync(h, otherEmail), StatusCodes.Status400BadRequest, "public_registrations.invalid_code");
        AssertProblem(await SubmitAsync(h, otherTournament), StatusCodes.Status400BadRequest, "public_registrations.invalid_code");
        AssertProblem(await SubmitAsync(h, "654321"), StatusCodes.Status400BadRequest, "public_registrations.invalid_code");
        Assert.Empty(h.R.Registrations.Writes);
    }

    // ---------- Submit: registration ----------

    [Fact]
    public async Task Submit_NewPlayers_CreatesThemAndAPendingPublicRegistration_AndConsumesTheCode()
    {
        var h = new PublicRegistrationsTestHarness();
        var tournament = await h.TournamentAsync(fee: 20000m);
        var code = await h.CodeAsync();

        var result = await Endpoints.SubmitAsync(
            Slug,
            new PublicRegistrationBody(" Contact@Club.test ", code, [Participant(2, "Bea", "Paz", "bea@club.test"), Participant(1, " Ana ", "Gil", " Ana@Club.test ", document: "30111222")], true),
            h.Sender,
            CancellationToken.None);

        var submitted = Assert.IsType<Created<PublicRegistrationSubmittedResponse>>(result).Value!;
        var stored = h.R.Registrations.Find(Guid.Parse(submitted.Reference))!;
        Assert.Equal(("pending", new PublicRegistrationPaymentResponse("pending", 20000m)), (submitted.Status, submitted.Payment));
        Assert.Equal(
            (RegistrationSource.Public, RegistrationStatus.Pending, RegistrationPaymentStatus.Pending, Contact, true, (string?)null),
            (stored.Source, stored.Status, stored.PaymentStatus, stored.ContactEmail, stored.PublicNameConsent, stored.Notes));
        Assert.Equal(tournament.Id, stored.TournamentId.Value);
        var players = stored.Participants.Select(p => h.R.Players.Find(p.PlayerId)!).ToList();
        Assert.Equal([("Ana", "Gil", "ana@club.test", "30111222"), ("Bea", "Paz", "bea@club.test", null)], players.Select(p => (p.FirstName, p.LastName, p.Email, p.DocumentId)));
        Assert.All(players, p => Assert.Equal((h.Organization, true, (Guid?)null), (p.OrganizationId.Value, p.IsActive, p.UserId?.Value)));
        Assert.Equal(players.Select(p => $"add:{p.Id}"), h.R.Players.Writes);
        Assert.Equal(["create"], h.R.Registrations.Writes);
        Assert.Empty(h.Cache.Entries);
    }

    [Fact]
    public async Task Submit_PlacesTakenByConfirmedAndPending_IsWaitlisted()
    {
        var h = new PublicRegistrationsTestHarness();
        var tournament = await h.TournamentAsync(maxPairs: 2);
        h.R.SeedRegistration(tournament, RegistrationStatus.Confirmed);
        h.R.SeedRegistration(tournament, RegistrationStatus.Pending);

        var submitted = Assert.IsType<Created<PublicRegistrationSubmittedResponse>>(await SubmitAsync(h, await h.CodeAsync())).Value!;

        Assert.Equal(("waitlisted", new PublicRegistrationPaymentResponse("notRequired", null)), (submitted.Status, submitted.Payment));
        Assert.Equal(RegistrationStatus.Waitlisted, h.R.Registrations.Find(Guid.Parse(submitted.Reference))!.Status);
    }

    [Fact]
    public async Task Submit_ClosedWindow_IsConflictWithoutWriting_AndKeepsTheCode()
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync();
        var code = await h.CodeAsync();
        h.Clock.UtcNow = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
        var before = h.StoreSnapshot();

        AssertProblem(await SubmitAsync(h, code), StatusCodes.Status409Conflict, "public_registrations.closed");
        Assert.Equal(before, h.StoreSnapshot());
    }

    [Fact]
    public async Task Submit_ExistingPlayerByEmail_IsReusedAsStored_WithoutFillingItsEmptyFields()
    {
        // L9: the stored data decides eligibility; the typed (here ineligible) data is neither read nor written.
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync(genderId: Female);
        var ana = h.SeedPlayer("Ana", "Gil", email: "ana@club.test", gender: Female);
        var before = PlayerSnapshot(h, ana);

        var submitted = Assert.IsType<Created<PublicRegistrationSubmittedResponse>>(await SubmitAsync(
            h,
            await h.CodeAsync(),
            new PublicParticipantBody(1, "Anita", "Gil Paz", "ANA@club.test", "+54 11 5555", "30111222", new DateOnly(1990, 5, 1), Male, Sixth, "Norte"),
            Participant(2, "Bea", "Paz", "bea@club.test") with { GenderId = Female })).Value!;

        Assert.Equal(before, PlayerSnapshot(h, ana));
        Assert.Equal(ana.Id, h.R.Registrations.Find(Guid.Parse(submitted.Reference))!.Participants[0].PlayerId);
        Assert.DoesNotContain(h.R.Players.Writes, w => w.EndsWith(ana.Id.ToString(), StringComparison.Ordinal));
        Assert.Null(await h.R.Players.FindByDocumentAsync(new OrganizationId(h.Organization), "30111222"));
    }

    public static TheoryData<string> MatchedPlayerRefusals => new()
    {
        "emailAndDocumentOfTwoPlayers",
        "inactivePlayer",
        "alreadyRegistered",
        "samePlayerInTwoSlots",
        "storedGenderMismatch",
        "storedCategoryMissing",
        "storedCategoryOutOfRange",
        "storedAgeOutOfRange",
        "storedBirthDateMissing",
    };

    [Theory]
    [MemberData(nameof(MatchedPlayerRefusals))]
    public async Task Submit_MatchedPlayerThatCannotRegister_IsOneGenericError_WithoutWritingOrChangingAnyPlayer(string cause)
    {
        // L9: every refusal that depends on a stored player's data or state is the same problem, whatever the cause.
        var h = new PublicRegistrationsTestHarness();
        var tournament = await h.TournamentAsync();
        SeedProfile(h, tournament, true, new EligibilitySlot(1, Female, Fourth, 18, null, null, true), new EligibilitySlot(2, Female, Fourth, 18, null, null, true));
        var ana = h.SeedPlayer(
            "Ana",
            "Gil",
            email: "ana@club.test",
            documentId: "30111222",
            birthDate: cause switch { "storedAgeOutOfRange" => new DateOnly(2012, 1, 1), "storedBirthDateMissing" => null, _ => new DateOnly(1990, 5, 1) },
            gender: cause == "storedGenderMismatch" ? Male : Female,
            category: cause switch { "storedCategoryMissing" => null, "storedCategoryOutOfRange" => Sixth, _ => Fourth },
            isActive: cause != "inactivePlayer");
        var eva = h.SeedPlayer("Eva", "Sol", documentId: "40111222", birthDate: new DateOnly(1990, 1, 1), gender: Female, category: Fourth);
        if (cause == "alreadyRegistered")
            h.R.SeedRegistration(tournament, RegistrationStatus.Waitlisted, ana, h.SeedPlayer("Flor", "Luz"));
        var typedAna = new PublicParticipantBody(1, "Ana", "Gil", "ana@club.test", "+54 11 5555", null, new DateOnly(1990, 5, 1), Female, Fourth, "Norte");
        var bea = new PublicParticipantBody(2, "Bea", "Paz", "bea@club.test", null, null, new DateOnly(1990, 1, 1), Female, Fourth);
        PublicParticipantBody[] participants = cause switch
        {
            "emailAndDocumentOfTwoPlayers" => [typedAna with { DocumentId = "40111222" }, bea],
            "samePlayerInTwoSlots" => [typedAna, bea with { Email = null, DocumentId = "30111222" }],
            _ => [typedAna, bea],
        };
        var code = await h.CodeAsync();
        var before = (h.StoreSnapshot(), PlayerSnapshot(h, ana), PlayerSnapshot(h, eva));

        var result = await Endpoints.SubmitAsync(Slug, new PublicRegistrationBody(Contact, code, participants), h.Sender, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(
            (StatusCodes.Status409Conflict, "Conflict", "public_registrations.errors.participant_unavailable"),
            (problem.StatusCode, problem.ProblemDetails.Title, problem.ProblemDetails.Detail));
        Assert.Equal(["code"], problem.ProblemDetails.Extensions.Keys);
        Assert.Equal("public_registrations.participant_unavailable", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(before, (h.StoreSnapshot(), PlayerSnapshot(h, ana), PlayerSnapshot(h, eva)));
        Assert.Single(h.Email.Sent);
    }

    [Fact]
    public async Task Submit_NewParticipantNextToARefusedMatchedPlayer_KeepsItsSpecificError()
    {
        // Errors about newly typed data stay specific (L9), even when a matched player would also be refused.
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync(genderId: Female);
        h.SeedPlayer("Ana", "Gil", email: "ana@club.test", gender: Male);
        var code = await h.CodeAsync();
        var before = h.StoreSnapshot();

        var result = await SubmitAsync(h, code, Participant(1, "Ana", "Gil", "ana@club.test"), Participant(2, "Bea", "Paz", "bea@club.test") with { GenderId = Male });

        AssertProblem(result, StatusCodes.Status400BadRequest, "registrations.gender_mismatch");
        Assert.Equal(before, h.StoreSnapshot());
    }

    [Fact]
    public async Task Submit_CompleteExistingPlayerByDocument_IsReusedWithoutAWrite()
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync();
        var ana = h.SeedPlayer("Ana", "Gil", "ana@club.test", "+54 11", "30111222", new DateOnly(1990, 5, 1), Female, Fourth, "Norte");

        var submitted = Assert.IsType<Created<PublicRegistrationSubmittedResponse>>(await SubmitAsync(
            h,
            await h.CodeAsync(),
            new PublicParticipantBody(1, "Eva", "Sol", null, "+54 99", " 30111222 ", new DateOnly(2001, 1, 1), Male, Sixth, "Sur"),
            Participant(2, "Bea", "Paz", "bea@club.test"))).Value!;

        Assert.Equal(ana.Id, h.R.Registrations.Find(Guid.Parse(submitted.Reference))!.Participants[0].PlayerId);
        Assert.DoesNotContain(h.R.Players.Writes, w => w.EndsWith(ana.Id.ToString(), StringComparison.Ordinal));
        var stored = h.R.Players.Find(ana.Id)!;
        Assert.Equal(("Ana", "+54 11", Female, "Norte"), (stored.FirstName, stored.Phone, stored.GenderId, stored.Club));
    }

    [Theory]
    [InlineData("samePersonTwice", 400, "registrations.duplicate_player")]
    [InlineData("sameEmailAnyCase", 400, "registrations.duplicate_player")]
    [InlineData("sameDocument", 400, "registrations.duplicate_player")]
    [InlineData("missingFirstName", 400, "players.first_name_required")]
    [InlineData("unknownGender", 400, "players.unknown_gender")]
    [InlineData("unknownCategory", 400, "players.unknown_category")]
    [InlineData("birthDateInFuture", 400, "players.birth_date_in_future")]
    [InlineData("noParticipants", 400, "registrations.participants_required")]
    [InlineData("onePlayerForDoubles", 400, "registrations.required_slot_missing")]
    [InlineData("thirdSlotForDoubles", 400, "registrations.invalid_slot")]
    [InlineData("sameSlotTwice", 400, "registrations.invalid_slot")]
    public async Task Submit_InvalidParticipants_AreRefusedWithoutWriting_AndKeepTheCode(string scenario, int status, string code)
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync();
        var bea = Participant(2, "Bea", "Paz", "bea@club.test");
        PublicParticipantBody[] participants = scenario switch
        {
            "samePersonTwice" => [Participant(1, "Bea", "Paz", "bea@club.test"), bea],
            "sameEmailAnyCase" => [Participant(1, "Bea", "Paz", " BEA@club.test"), bea],
            "sameDocument" => [Participant(1, "Eva", "Sol", document: "50111222"), Participant(2, "Eva", "Sol", document: " 50111222 ")],
            "missingFirstName" => [Participant(1, " ", "Gil", "new@club.test"), bea],
            "unknownGender" => [Participant(1, "Ana", "Gil", "new@club.test") with { GenderId = Guid.NewGuid() }, bea],
            "unknownCategory" => [Participant(1, "Ana", "Gil", "new@club.test") with { CategoryId = Guid.NewGuid() }, bea],
            "birthDateInFuture" => [Participant(1, "Ana", "Gil", "new@club.test") with { BirthDate = new DateOnly(2027, 1, 1) }, bea],
            "noParticipants" => [],
            "onePlayerForDoubles" => [bea],
            "sameSlotTwice" => [Participant(2, "Ana", "Gil", "new@club.test"), bea],
            "thirdSlotForDoubles" => [Participant(1, "Ana", "Gil", "new@club.test"), bea, Participant(3, "Eva", "Luz", "eva@club.test")],
            _ => [Participant(1, "Ana", "Gil", "ana@club.test"), bea],
        };
        var codeSent = await h.CodeAsync();
        var before = h.StoreSnapshot();

        var result = await Endpoints.SubmitAsync(Slug, new PublicRegistrationBody(Contact, codeSent, participants), h.Sender, CancellationToken.None);

        AssertProblem(result, status, code);
        Assert.Equal(before, h.StoreSnapshot());
    }

    [Theory]
    [InlineData(false, null, null)]
    [InlineData(true, "Male", "registrations.gender_mismatch")]
    [InlineData(true, null, "registrations.category_out_of_range")]
    [InlineData(true, "Third", null)]
    public async Task Submit_Eligibility_AppliesTheTournamentsRules_AndMissingDataFails(bool restrict, string? data, string? code)
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync(genderId: restrict ? Female : null, categoryId: restrict ? Fourth : null);
        var first = data switch
        {
            "Male" => Participant(1, "Ana", "Gil", "ana@club.test") with { GenderId = Male, CategoryId = Fourth },
            "Third" => Participant(1, "Ana", "Gil", "ana@club.test") with { GenderId = Female, CategoryId = Third },
            _ => Participant(1, "Ana", "Gil", "ana@club.test") with { GenderId = Female },
        };
        var second = Participant(2, "Bea", "Paz", "bea@club.test") with { GenderId = Female, CategoryId = Fourth };
        var codeSent = await h.CodeAsync();
        var before = h.StoreSnapshot();

        var result = await SubmitAsync(h, codeSent, first, second);

        if (code is null)
        {
            Assert.IsType<Created<PublicRegistrationSubmittedResponse>>(result);
            return;
        }

        AssertProblem(result, StatusCodes.Status400BadRequest, code);
        Assert.Equal(before, h.StoreSnapshot());
    }

    [Theory]
    [InlineData(true, "registrations.age_out_of_range")]
    [InlineData(false, null)]
    public async Task Submit_Profile_ActiveSlotsApply_InactiveFallsBackToTheTournamentsRules(bool active, string? code)
    {
        // L8: the profile demands adults; the tournament itself has no age rule.
        var h = new PublicRegistrationsTestHarness();
        var tournament = await h.TournamentAsync();
        SeedProfile(h, tournament, active, new EligibilitySlot(1, null, null, 18, null, null, true), new EligibilitySlot(2, null, null, 18, null, null, true));
        var codeSent = await h.CodeAsync();

        var result = await SubmitAsync(
            h,
            codeSent,
            Participant(1, "Ana", "Gil", "ana@club.test") with { BirthDate = new DateOnly(2012, 1, 1) },
            Participant(2, "Bea", "Paz", "bea@club.test") with { BirthDate = new DateOnly(1990, 1, 1) });

        if (code is null)
        {
            Assert.IsType<Created<PublicRegistrationSubmittedResponse>>(result);
            return;
        }

        AssertProblem(result, StatusCodes.Status400BadRequest, code);
        Assert.Empty(h.R.Players.Writes);
        Assert.Empty(h.R.Registrations.Writes);
    }

    [Fact]
    public async Task Submit_ProfileTheOrganizationLacks_AdmitsNothing()
    {
        var h = new PublicRegistrationsTestHarness();
        var tournament = await h.TournamentAsync();
        var foreign = EligibilityProfile.Reconstitute(
            EligibilityProfileId.New(), new OrganizationId(Guid.NewGuid()), "Foreign", null, true,
            [new EligibilitySlot(1, null, null, null, null, null, true)], Now, Now);
        h.R.Profiles.Seed(foreign);
        h.R.Profiles.TournamentProfiles[tournament.Id] = foreign.Id;

        AssertProblem(await SubmitAsync(h, await h.CodeAsync()), StatusCodes.Status404NotFound, "eligibility_profiles.not_found");
        AssertProblem(
            await Endpoints.GetInfoAsync(Slug, new DefaultHttpContext(), h.Sender, CancellationToken.None),
            StatusCodes.Status404NotFound,
            "eligibility_profiles.not_found");
        Assert.Empty(h.R.Players.Writes);
        Assert.Empty(h.R.Registrations.Writes);
    }

    // ---------- Confirmed pairs ----------

    [Fact]
    public async Task ConfirmedPairs_ListOnlyConfirmed_NamesWithConsent_OtherwiseAnAnonymousLabel()
    {
        var h = new PublicRegistrationsTestHarness();
        var tournament = await h.TournamentAsync();
        var named = await SubmittedAsync(h, true, Participant(1, "Ana", "Gil", "ana@club.test"), Participant(2, "Bea", "Paz", "bea@club.test"));
        h.Clock.UtcNow = Now.AddMinutes(2);
        var anonymous = await SubmittedAsync(h, false, Participant(1, "Eva", "Sol", "eva@club.test"), Participant(2, "Flor", "Luz", "flor@club.test"));
        h.R.SeedRegistration(tournament, RegistrationStatus.Pending);
        h.R.Registrations.SeedChange(named, RegistrationStatus.Confirmed);
        h.R.Registrations.SeedChange(anonymous, RegistrationStatus.Confirmed);
        var context = new DefaultHttpContext();

        var pairs = Assert.IsType<Ok<PublicConfirmedPairsResponse>>(
            await Endpoints.GetConfirmedPairsAsync(Slug, context, h.Sender, CancellationToken.None)).Value!;

        Assert.Equal(2, pairs.Items.Count);
        Assert.Equal((1, "Ana Gil / Bea Paz", (string?)null), (pairs.Items[0].Position, pairs.Items[0].DisplayName, pairs.Items[0].LabelKey));
        Assert.Equal(["Ana Gil", "Bea Paz"], pairs.Items[0].Members);
        Assert.Equal((2, (string?)null, "public_registrations.confirmed_pairs.anonymous"), (pairs.Items[1].Position, pairs.Items[1].DisplayName, pairs.Items[1].LabelKey));
        Assert.Empty(pairs.Items[1].Members);
        Assert.Equal("public, max-age=60", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task EveryPublicResponse_CarriesNoPlayerIdsEmailsPhonesOrDocuments()
    {
        var h = new PublicRegistrationsTestHarness();
        await h.TournamentAsync(fee: 1000m);
        var existing = h.SeedPlayer("Ana", "Gil", "ana@club.test", "+54 11 1111", "30111222", new DateOnly(1990, 5, 1));
        var code = await h.CodeAsync();
        var submitted = await Endpoints.SubmitAsync(
            Slug,
            new PublicRegistrationBody(Contact, code, [Participant(1, "Ana", "Gil", "ana@club.test"), Participant(2, "Bea", "Paz", "bea@club.test", "+54 11 2222", "30111333")], true),
            h.Sender,
            CancellationToken.None);
        var reference = Assert.IsType<Created<PublicRegistrationSubmittedResponse>>(submitted).Value!.Reference;
        h.R.Registrations.SeedChange(Guid.Parse(reference), RegistrationStatus.Confirmed);
        h.Clock.UtcNow = Now.AddMinutes(5);

        var bodies = new object?[]
        {
            Assert.IsType<Ok<PublicRegistrationInfoResponse>>(await Endpoints.GetInfoAsync(Slug, new DefaultHttpContext(), h.Sender, CancellationToken.None)).Value,
            Assert.IsType<Accepted<PublicRegistrationCodeResponse>>(await Endpoints.SendCodeAsync(Slug, new PublicRegistrationCodeBody(Contact), h.Sender, CancellationToken.None)).Value,
            Assert.IsType<Created<PublicRegistrationSubmittedResponse>>(submitted).Value,
            Assert.IsType<Ok<PublicConfirmedPairsResponse>>(await Endpoints.GetConfirmedPairsAsync(Slug, new DefaultHttpContext(), h.Sender, CancellationToken.None)).Value,
        };
        var playerIds = h.R.Registrations.Find(Guid.Parse(reference))!.Participants.Select(p => p.PlayerId.Value.ToString()).Append(existing.Id.Value.ToString());

        foreach (var json in bodies.Select(body => JsonSerializer.Serialize(body)))
        {
            foreach (var secret in playerIds.Concat(["ana@club.test", "bea@club.test", Contact, "+54 11", "30111", "1990-05-01", h.Organization.ToString()]))
                Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------- Mapping ----------

    [Fact]
    public void PublicRegistrationEndpoints_AreAnonymousDocumentedAndTheWritesAreRateLimited()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(new PublicRegistrationsTestHarness().Sender);
        var app = builder.Build();
        app.MapPublicRegistrationsEndpoints();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToList();

        const string Prefix = "/api/public/tournaments/{slug}";
        Assert.Equal(
            [$"GET {Prefix}/confirmed-pairs", $"GET {Prefix}/registration", $"POST {Prefix}/registration", $"POST {Prefix}/registration/code"],
            endpoints.Select(e => $"{e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single()} {e.RoutePattern.RawText}").Order(StringComparer.Ordinal));
        foreach (var endpoint in endpoints)
        {
            Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.False(string.IsNullOrWhiteSpace(endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary));
            Assert.False(string.IsNullOrWhiteSpace(endpoint.Metadata.GetMetadata<IEndpointDescriptionMetadata>()?.Description));
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(), p => p.StatusCode == StatusCodes.Status404NotFound);
        }

        string? Policy(string route) => endpoints.Single(e => $"{e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single()} {e.RoutePattern.RawText}" == route)
            .Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        Assert.Equal(ApiRateLimitPolicies.PublicRegistrationCode, Policy($"POST {Prefix}/registration/code"));
        Assert.Equal(ApiRateLimitPolicies.AnonymousAuth, Policy($"POST {Prefix}/registration"));
        Assert.Null(Policy($"GET {Prefix}/registration"));
    }

    // ---------- Helpers ----------

    private static PublicParticipantBody Participant(int slot, string first, string last, string? email = null, string? phone = null, string? document = null) =>
        new(slot, first, last, email, phone, document);

    private static Task<IResult> SubmitAsync(PublicRegistrationsTestHarness h, string code, params PublicParticipantBody[] participants) =>
        Endpoints.SubmitAsync(
            Slug,
            new PublicRegistrationBody(Contact, code, participants.Length > 0 ? participants : [Participant(1, "Ana", "Gil", "ana@club.test"), Participant(2, "Bea", "Paz", "bea@club.test")]),
            h.Sender,
            CancellationToken.None);

    /// <summary>Submits through the real flow and returns the registration id.</summary>
    private static async Task<Guid> SubmittedAsync(PublicRegistrationsTestHarness h, bool consent, params PublicParticipantBody[] participants)
    {
        var code = await h.CodeAsync($"{Guid.NewGuid():N}@club.test");
        var email = h.Templates.Composed[^1].To;
        var result = await Endpoints.SubmitAsync(Slug, new PublicRegistrationBody(email, code, participants, consent), h.Sender, CancellationToken.None);
        return Guid.Parse(Assert.IsType<Created<PublicRegistrationSubmittedResponse>>(result).Value!.Reference);
    }

    private static void SeedProfile(PublicRegistrationsTestHarness h, ZonarHub.Application.Abstractions.TournamentAdminDto tournament, bool active, params EligibilitySlot[] slots)
    {
        var profile = EligibilityProfile.Reconstitute(
            EligibilityProfileId.New(), new OrganizationId(tournament.OrganizationId), "Profile", null, active, slots, Now, Now);
        h.R.Profiles.Seed(profile);
        h.R.Profiles.TournamentProfiles[tournament.Id] = profile.Id;
    }

    /// <summary>Every stored value of the player, to prove the public flow left it byte-for-byte unchanged.</summary>
    private static string PlayerSnapshot(PublicRegistrationsTestHarness h, Player player) => JsonSerializer.Serialize(h.R.Players.Find(player.Id));

    private static DateOnly Day(string value) => DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static Task<IResult> InvokeAsync(string operation, PublicRegistrationsTestHarness h, string slug, string code) => operation switch
    {
        "info" => Endpoints.GetInfoAsync(slug, new DefaultHttpContext(), h.Sender, CancellationToken.None),
        "code" => Endpoints.SendCodeAsync(slug, new PublicRegistrationCodeBody(Contact), h.Sender, CancellationToken.None),
        "submit" => Endpoints.SubmitAsync(slug, new PublicRegistrationBody(Contact, code, [Participant(1, "Ana", "Gil"), Participant(2, "Bea", "Paz")]), h.Sender, CancellationToken.None),
        _ => Endpoints.GetConfirmedPairsAsync(slug, new DefaultHttpContext(), h.Sender, CancellationToken.None),
    };
}
