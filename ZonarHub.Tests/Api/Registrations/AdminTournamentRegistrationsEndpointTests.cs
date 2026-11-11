using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.EligibilityProfiles;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.Players;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.Tournaments;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Application.Features.TournamentRegistrations;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Registrations;
using ZonarHub.Tests.Application.Registrations;
using static ZonarHub.Tests.Api.Registrations.AdminPlayersEndpointTests;
using static ZonarHub.Tests.Application.Registrations.RegistrationsTestHarness;
using Endpoints = ZonarHub.ApiService.Endpoints.Admin.Circuit.Tournaments.AdminTournamentRegistrationsEndpointsExtensions;

namespace ZonarHub.Tests.Api.Registrations;

/// <summary>
/// Tournament registrations (S4, D2, D3, D5) through the real handlers: tenant rule, D3 eligibility before any write, the
/// D2 status and capacity rules (checked first, re-applied by the store), stale revisions, manual payment marks and the
/// draw participants of the confirmed registrations.
/// </summary>
public class AdminTournamentRegistrationsEndpointTests
{
    public static TheoryData<string> Operations => new()
    {
        "list", "get", "create", "confirm", "reject", "waitlist", "cancel", "payment", "drawParticipants",
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task EveryEndpoint_AnotherTenantsTournament_IsForbiddenWithoutWriting(string operation)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.ForeignOrganizationAsync();
        var tournament = await h.TournamentAsync(org);
        var registration = h.SeedRegistration(tournament, RegistrationStatus.Pending);

        var result = await InvokeAsync(operation, h, tournament.Id, registration);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal(AdminTournamentErrors.Forbidden.Code, problem.ProblemDetails.Extensions["code"]);
        AssertNothingWritten(h, registration);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task EveryEndpoint_UnknownTournament_IsNotFoundWithoutWriting(string operation)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var registration = h.SeedRegistration(await h.TournamentAsync(org), RegistrationStatus.Pending);

        var result = await InvokeAsync(operation, h, Guid.NewGuid(), registration);

        AssertProblem(result, StatusCodes.Status404NotFound, AdminTournamentErrors.NotFound.Code);
        AssertNothingWritten(h, registration);
    }

    [Fact]
    public async Task Create_StoresAPendingAdminRegistrationWithThePlayersInSlotOrder()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org, fee: 20000m);
        var ana = h.SeedPlayer(org, "Ana", "Gil", club: "Norte");
        var bea = h.SeedPlayer(org, "Bea", "Paz");

        var result = await Endpoints.CreateAsync(
            tournament.Id,
            new CreateRegistrationBody([new(2, bea.Id.Value), new(1, ana.Id.Value)], null, " Ana@Club.test ", " cash ", true),
            h.Sender,
            CancellationToken.None);

        var created = Assert.IsType<Created<TournamentRegistrationResponse>>(result);
        var registration = created.Value!;
        Assert.Equal($"/api/admin/tournaments/{tournament.Id}/registrations/{registration.Id}", created.Location);
        Assert.Equal(("pending", "admin", "pending"), (registration.Status, registration.Source, registration.PaymentStatus));
        Assert.Equal(("ana@club.test", "cash", true, 0), (registration.ContactEmail, registration.Notes, registration.PublicNameConsent, registration.Revision));
        Assert.Equal(
            [new RegistrationParticipantResponse(1, ana.Id.Value, "Ana", "Gil", "Ana Gil", "Norte"), new RegistrationParticipantResponse(2, bea.Id.Value, "Bea", "Paz", "Bea Paz", null)],
            registration.Participants);
        Assert.Equal(["create"], h.Registrations.Writes);
        var read = Assert.IsType<Ok<TournamentRegistrationResponse>>(
            await Endpoints.GetAsync(tournament.Id, registration.Id, h.Sender, CancellationToken.None)).Value!;
        Assert.Equal(registration.Participants, read.Participants);
        Assert.Equal(RegistrationSource.Admin, Assert.Single(h.Registrations.CreatedRequests).Source);
    }

    [Fact]
    public async Task Create_ConfirmedRequestWithoutFee_IsConfirmedAndNeedsNoPayment()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org, maxPairs: 1);

        var registration = Created(await Endpoints.CreateAsync(tournament.Id, Pair(h, org, "confirmed"), h.Sender, CancellationToken.None));

        Assert.Equal(("confirmed", "notRequired"), (registration.Status, registration.PaymentStatus));
    }

    [Fact]
    public async Task Create_PendingRequestWhenPlacesAreTaken_IsWaitlistedByTheStore()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org, maxPairs: 2);
        h.SeedRegistration(tournament, RegistrationStatus.Confirmed);
        h.SeedRegistration(tournament, RegistrationStatus.Pending);

        var registration = Created(await Endpoints.CreateAsync(tournament.Id, Pair(h, org), h.Sender, CancellationToken.None));

        Assert.Equal("waitlisted", registration.Status);
    }

    [Theory]
    [InlineData("waitlisted", 400, "registrations.invalid_status")]
    [InlineData("noParticipants", 400, "registrations.participants_required")]
    [InlineData("duplicateSlot", 400, "registrations.invalid_slot")]
    [InlineData("contactEmail", 400, "registrations.invalid_contact_email")]
    [InlineData("foreignPlayer", 404, "registrations.player_not_found")]
    [InlineData("inactivePlayer", 404, "registrations.player_not_found")]
    [InlineData("onePlayerForDoubles", 400, "registrations.required_slot_missing")]
    [InlineData("thirdSlotForDoubles", 400, "registrations.invalid_slot")]
    public async Task Create_InvalidRequest_IsRefusedBeforeAnyStoreWrite(string scenario, int status, string code)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org);
        var ana = h.SeedPlayer(org, "Ana", "Gil");
        var bea = h.SeedPlayer(org, "Bea", "Paz");
        var other = scenario == "foreignPlayer"
            ? h.SeedPlayer(Guid.NewGuid(), "Eva", "Sol")
            : h.SeedPlayer(org, "Eva", "Sol", isActive: scenario != "inactivePlayer");
        var body = scenario switch
        {
            "waitlisted" => new CreateRegistrationBody([new(1, ana.Id.Value), new(2, bea.Id.Value)], "waitlisted"),
            "noParticipants" => new CreateRegistrationBody([]),
            "duplicateSlot" => new CreateRegistrationBody([new(1, ana.Id.Value), new(1, bea.Id.Value)]),
            "contactEmail" => new CreateRegistrationBody([new(1, ana.Id.Value), new(2, bea.Id.Value)], ContactEmail: "nope"),
            "onePlayerForDoubles" => new CreateRegistrationBody([new(1, ana.Id.Value)]),
            "thirdSlotForDoubles" => new CreateRegistrationBody([new(1, ana.Id.Value), new(2, bea.Id.Value), new(3, other.Id.Value)]),
            _ => new CreateRegistrationBody([new(1, ana.Id.Value), new(2, other.Id.Value)]),
        };

        var result = await Endpoints.CreateAsync(tournament.Id, body, h.Sender, CancellationToken.None);

        AssertProblem(result, status, code);
        Assert.Empty(h.Registrations.Writes);
        Assert.Empty(h.Registrations.RejectedWrites);
    }

    [Theory]
    [InlineData("male", null, true, null)]
    [InlineData("female", null, false, "registrations.gender_mismatch")]
    [InlineData("mixed", null, true, null)]
    [InlineData(null, "third", true, null)]
    [InlineData(null, "sixth", false, "registrations.category_out_of_range")]
    public async Task Create_WithoutProfile_AppliesTheTournamentsGenderAndCategory(
        string? tournamentGender, string? tournamentCategory, bool eligible, string? code)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var gender = tournamentGender switch { "male" => Male, "female" => Female, "mixed" => Mixed, _ => (Guid?)null };
        var category = tournamentCategory switch { "third" => Third, "sixth" => Sixth, _ => (Guid?)null };
        var tournament = await h.TournamentAsync(org, genderId: gender, categoryId: category);
        var ana = h.SeedPlayer(org, "Ana", "Gil", gender: Male, category: Fourth);
        var bea = h.SeedPlayer(org, "Bea", "Paz", gender: Male, category: Fourth);

        var result = await Endpoints.CreateAsync(
            tournament.Id, new CreateRegistrationBody([new(1, ana.Id.Value), new(2, bea.Id.Value)]), h.Sender, CancellationToken.None);

        if (eligible)
        {
            Created(result);
            return;
        }

        AssertProblem(result, StatusCodes.Status400BadRequest, code!);
        Assert.Empty(h.Registrations.Writes);
    }

    [Fact]
    public async Task Create_SingleModality_TakesOneParticipant()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org, modalityId: SingleModality);
        var ana = h.SeedPlayer(org, "Ana", "Gil");

        Created(await Endpoints.CreateAsync(tournament.Id, new CreateRegistrationBody([new(1, ana.Id.Value)]), h.Sender, CancellationToken.None));
    }

    [Theory]
    [InlineData("1990-01-01", true, null)]
    [InlineData("2010-01-01", false, "registrations.age_out_of_range")]
    public async Task Create_WithProfile_AppliesItsSlotsInsteadOfTheTournaments(string birthDate, bool eligible, string? code)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org, modalityId: SingleModality, genderId: Female);
        var profile = AdminEligibilityProfilesEndpointTests.Seed(h, org, "Adultos",
            new EligibilitySlot(1, Male, null, 18, null, null, true),
            new EligibilitySlot(2, null, null, 18, null, null, true));
        h.Profiles.TournamentProfiles[tournament.Id] = profile.Id;
        var born = DateOnly.Parse(birthDate, System.Globalization.CultureInfo.InvariantCulture);
        var ana = h.SeedPlayer(org, "Ana", "Gil", gender: Male, birthDate: new DateOnly(1990, 1, 1));
        var bea = h.SeedPlayer(org, "Bea", "Paz", birthDate: born);

        var result = await Endpoints.CreateAsync(
            tournament.Id, new CreateRegistrationBody([new(1, ana.Id.Value), new(2, bea.Id.Value)]), h.Sender, CancellationToken.None);

        if (eligible)
        {
            Created(result);
            return;
        }

        AssertProblem(result, StatusCodes.Status400BadRequest, code!);
        Assert.Empty(h.Registrations.Writes);
    }

    [Fact]
    public async Task Create_TournamentWithoutModality_TakesAPair()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.Tournaments.Tournaments.UpdateAsync(await h.TournamentAsync(org) with { ModalityId = null });
        var ana = h.SeedPlayer(org, "Ana", "Gil");

        var single = await Endpoints.CreateAsync(tournament.Id, new CreateRegistrationBody([new(1, ana.Id.Value)]), h.Sender, CancellationToken.None);

        AssertProblem(single, StatusCodes.Status400BadRequest, "registrations.required_slot_missing");
        Created(await Endpoints.CreateAsync(tournament.Id, Pair(h, org), h.Sender, CancellationToken.None));
    }

    [Theory]
    [InlineData("female", true, null)]
    [InlineData("male", false, "registrations.gender_mismatch")]
    public async Task Create_InactiveProfile_IsIgnoredAndTheTournamentsRulesApply(string players, bool eligible, string? code)
    {
        // L8: the inactive profile would demand a male first player and admit any second one; the tournament demands women.
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org, genderId: Female);
        var profile = EligibilityProfile.Reconstitute(
            EligibilityProfileId.New(), new ZonarHub.Domain.Organizations.OrganizationId(org), "Inactive", null, false,
            [new EligibilitySlot(1, Male, null, null, null, null, true), new EligibilitySlot(2, null, null, null, null, null, true)], Now, Now);
        h.Profiles.Seed(profile);
        h.Profiles.TournamentProfiles[tournament.Id] = profile.Id;
        var gender = players == "female" ? Female : Male;
        var ana = h.SeedPlayer(org, "Ana", "Gil", gender: gender);
        var bea = h.SeedPlayer(org, "Bea", "Paz", gender: gender);

        var result = await Endpoints.CreateAsync(
            tournament.Id, new CreateRegistrationBody([new(1, ana.Id.Value), new(2, bea.Id.Value)]), h.Sender, CancellationToken.None);

        if (eligible)
        {
            Assert.Equal("pending", Created(result).Status);
            Assert.Equal(["create"], h.Registrations.Writes);
            return;
        }

        AssertProblem(result, StatusCodes.Status400BadRequest, code!);
        Assert.Empty(h.Registrations.Writes);
        Assert.Empty(h.Registrations.RejectedWrites);
    }

    [Fact]
    public async Task Create_ReferencedProfileTheOrganizationLacks_AdmitsNothing()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org);
        var foreign = AdminEligibilityProfilesEndpointTests.Seed(h, Guid.NewGuid(), "Foreign");
        h.Profiles.TournamentProfiles[tournament.Id] = foreign.Id;

        var result = await Endpoints.CreateAsync(tournament.Id, Pair(h, org), h.Sender, CancellationToken.None);

        AssertProblem(result, StatusCodes.Status404NotFound, "eligibility_profiles.not_found");
        Assert.Empty(h.Registrations.Writes);
    }

    [Theory]
    [InlineData("alreadyRegistered", "registrations.player_already_registered")]
    [InlineData("noPlace", "registrations.capacity_reached")]
    public async Task Create_StoreRefusal_IsConflictWithoutWriting(string scenario, string code)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org, maxPairs: 1);
        var ana = h.SeedPlayer(org, "Ana", "Gil");
        var bea = h.SeedPlayer(org, "Bea", "Paz");
        if (scenario == "alreadyRegistered")
            h.SeedRegistration(tournament, RegistrationStatus.Pending, ana, h.SeedPlayer(org, "Eva", "Sol"));
        else
            h.SeedRegistration(tournament, RegistrationStatus.Confirmed);

        var result = await Endpoints.CreateAsync(
            tournament.Id, new CreateRegistrationBody([new(1, ana.Id.Value), new(2, bea.Id.Value)], "confirmed"), h.Sender, CancellationToken.None);

        AssertProblem(result, StatusCodes.Status409Conflict, code);
        Assert.Empty(h.Registrations.Writes);
        Assert.Equal(code, Assert.Single(h.Registrations.RejectedWrites).Code);
    }

    [Theory]
    [InlineData("confirm", RegistrationStatus.Pending, "confirmed")]
    [InlineData("confirm", RegistrationStatus.Waitlisted, "confirmed")]
    [InlineData("reject", RegistrationStatus.Pending, "rejected")]
    [InlineData("waitlist", RegistrationStatus.Pending, "waitlisted")]
    [InlineData("cancel", RegistrationStatus.Confirmed, "cancelled")]
    public async Task Transition_AllowedByD2_IsWrittenAtTheNextRevision(string operation, RegistrationStatus from, string expected)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org, maxPairs: 2);
        var registration = h.SeedRegistration(tournament, from);

        var result = await InvokeAsync(operation, h, tournament.Id, registration, revision: 0);

        var updated = Assert.IsType<Ok<TournamentRegistrationResponse>>(result).Value!;
        Assert.Equal((expected, 1), (updated.Status, updated.Revision));
        Assert.Single(h.Registrations.Writes);
    }

    [Theory]
    [InlineData("confirm", RegistrationStatus.Rejected, "registrations.invalid_transition")]
    [InlineData("waitlist", RegistrationStatus.Confirmed, "registrations.invalid_transition")]
    [InlineData("cancel", RegistrationStatus.Cancelled, "registrations.invalid_transition")]
    [InlineData("confirm", RegistrationStatus.Pending, "registrations.capacity_reached")]
    public async Task Transition_RefusedByD2_IsConflictBeforeAnyStoreWrite(string operation, RegistrationStatus from, string code)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org, maxPairs: 1);
        h.SeedRegistration(tournament, RegistrationStatus.Confirmed);
        var registration = h.SeedRegistration(tournament, from);

        var result = await InvokeAsync(operation, h, tournament.Id, registration);

        AssertProblem(result, StatusCodes.Status409Conflict, code);
        AssertNothingWritten(h, registration);
    }

    [Fact]
    public async Task Transition_StaleRevision_IsConcurrentModificationWithoutWriting()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org);
        var registration = h.SeedRegistration(tournament, RegistrationStatus.Pending);
        h.Registrations.SeedChange(registration.Registration.Id.Value, payment: RegistrationPaymentStatus.Paid);

        var result = await InvokeAsync("confirm", h, tournament.Id, registration, revision: 0);

        AssertProblem(result, StatusCodes.Status409Conflict, "registrations.concurrent_modification");
        Assert.Empty(h.Registrations.Writes);
        Assert.Equal(RegistrationStatus.Pending, h.Registrations.Find(registration.Registration.Id.Value)!.Status);
    }

    [Fact]
    public async Task Transition_ChangedBetweenReadAndWrite_IsRefusedByTheStore()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org);
        var registration = h.SeedRegistration(tournament, RegistrationStatus.Pending);
        var id = registration.Registration.Id.Value;
        h.Registrations.BeforeNextWrite = () => h.Registrations.SeedChange(id, RegistrationStatus.Cancelled);

        var result = await InvokeAsync("confirm", h, tournament.Id, registration);

        AssertProblem(result, StatusCodes.Status409Conflict, "registrations.concurrent_modification");
        Assert.Empty(h.Registrations.Writes);
        Assert.Equal("registrations.concurrent_modification", Assert.Single(h.Registrations.RejectedWrites).Code);
        Assert.Equal(RegistrationStatus.Cancelled, h.Registrations.Find(id)!.Status);
    }

    [Fact]
    public async Task Transition_RegistrationOfAnotherTournament_IsNotFound()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org);
        var elsewhere = h.SeedRegistration(await h.TournamentAsync(org), RegistrationStatus.Pending);

        AssertProblem(await InvokeAsync("confirm", h, tournament.Id, elsewhere), StatusCodes.Status404NotFound, "registrations.not_found");
        AssertProblem(await InvokeAsync("payment", h, tournament.Id, elsewhere), StatusCodes.Status404NotFound, "registrations.not_found");
        AssertProblem(await InvokeAsync("get", h, tournament.Id, elsewhere), StatusCodes.Status404NotFound, "registrations.not_found");
        AssertNothingWritten(h, elsewhere);
    }

    [Fact]
    public async Task Payment_MarksPaidThenRefunded()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org);
        var registration = h.SeedRegistration(tournament, RegistrationStatus.Confirmed);
        var id = registration.Registration.Id.Value;
        h.Registrations.SeedChange(id, payment: RegistrationPaymentStatus.Pending);

        var paid = Assert.IsType<Ok<TournamentRegistrationResponse>>(await Endpoints.SetPaymentAsync(
            tournament.Id, id, new RegistrationPaymentBody("paid", 1), h.Sender, CancellationToken.None)).Value!;
        var refunded = Assert.IsType<Ok<TournamentRegistrationResponse>>(await Endpoints.SetPaymentAsync(
            tournament.Id, id, new RegistrationPaymentBody("refunded"), h.Sender, CancellationToken.None)).Value!;

        Assert.Equal(("paid", 2), (paid.PaymentStatus, paid.Revision));
        Assert.Equal(("refunded", "confirmed", 3), (refunded.PaymentStatus, refunded.Status, refunded.Revision));
        Assert.Equal(["payment:Paid", "payment:Refunded"], h.Registrations.Writes);
    }

    [Theory]
    [InlineData("failed", null, 400, "registrations.invalid_payment_status")]
    [InlineData(null, null, 400, "registrations.invalid_payment_status")]
    [InlineData("Paid", null, 400, "registrations.invalid_payment_status")]
    [InlineData("refunded", null, 409, "registrations.invalid_payment_transition")]
    [InlineData("paid", 0, 409, "registrations.concurrent_modification")]
    public async Task Payment_Refused_LeavesThePaymentUnchanged(string? paymentStatus, int? revision, int status, string code)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org);
        var registration = h.SeedRegistration(tournament, RegistrationStatus.Pending);
        var id = registration.Registration.Id.Value;
        h.Registrations.SeedChange(id, payment: RegistrationPaymentStatus.Pending);

        var result = await Endpoints.SetPaymentAsync(tournament.Id, id, new RegistrationPaymentBody(paymentStatus, revision), h.Sender, CancellationToken.None);

        AssertProblem(result, status, code);
        Assert.Empty(h.Registrations.Writes);
        Assert.Equal(RegistrationPaymentStatus.Pending, h.Registrations.Find(id)!.PaymentStatus);
    }

    [Fact]
    public async Task List_ReturnsTheRegistrationsInOrderWithTheCapacity()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org, maxPairs: 3);
        var first = h.SeedRegistration(tournament, RegistrationStatus.Confirmed);
        var second = h.SeedRegistration(tournament, RegistrationStatus.Pending);
        var third = h.SeedRegistration(tournament, RegistrationStatus.Waitlisted);
        h.SeedRegistration(tournament, RegistrationStatus.Rejected);
        h.SeedRegistration(await h.TournamentAsync(org), RegistrationStatus.Confirmed);

        var all = List(await Endpoints.ListAsync(tournament.Id, null, h.Sender, CancellationToken.None));
        var pending = List(await Endpoints.ListAsync(tournament.Id, "pending", h.Sender, CancellationToken.None));

        Assert.Equal(4, all.Items.Count);
        Assert.Equal([first.Registration.Id.Value, second.Registration.Id.Value, third.Registration.Id.Value], all.Items.Take(3).Select(r => r.Id));
        Assert.Equal(new RegistrationCapacityResponse(3, 1, 1, 1, 2), all.Capacity);
        Assert.Equal([second.Registration.Id.Value], pending.Items.Select(r => r.Id));
        Assert.Equal(all.Capacity, pending.Capacity);
        AssertProblem(await Endpoints.ListAsync(tournament.Id, "paid", h.Sender, CancellationToken.None),
            StatusCodes.Status400BadRequest, "registrations.invalid_status");
    }

    [Fact]
    public async Task DrawParticipants_MapTheConfirmedRegistrationsAsD5States()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org);
        var sameClub = h.SeedRegistration(tournament, RegistrationStatus.Confirmed,
            h.SeedPlayer(org, "Ana", "Gil", club: "Norte"), h.SeedPlayer(org, "Bea", "Paz", club: "Norte"));
        h.SeedRegistration(tournament, RegistrationStatus.Pending);
        var mixedClubs = h.SeedRegistration(tournament, RegistrationStatus.Confirmed,
            h.SeedPlayer(org, "Eva", "Sol", club: "Sur"), h.SeedPlayer(org, "Flor", "Rey", club: "Norte"));
        var noClub = h.SeedRegistration(tournament, RegistrationStatus.Confirmed,
            h.SeedPlayer(org, "Gala", "Luz"), h.SeedPlayer(org, "Ines", "Mar", club: "Este"));
        h.SeedRegistration(tournament, RegistrationStatus.Cancelled);

        var result = await Endpoints.DrawParticipantsAsync(tournament.Id, h.Sender, CancellationToken.None);

        var participants = Assert.IsType<Ok<IReadOnlyList<DrawParticipantBody>>>(result).Value!;
        Assert.Equal(
            [
                new DrawParticipantBody(sameClub.Registration.Id.Value.ToString(), "Ana Gil / Bea Paz", ["Ana Gil", "Bea Paz"], null, "Norte"),
                new DrawParticipantBody(mixedClubs.Registration.Id.Value.ToString(), "Eva Sol / Flor Rey", ["Eva Sol", "Flor Rey"], null, "Sur"),
                new DrawParticipantBody(noClub.Registration.Id.Value.ToString(), "Gala Luz / Ines Mar", ["Gala Luz", "Ines Mar"], null, null),
            ],
            participants,
            new DrawParticipantComparer());
    }

    [Fact]
    public void RegistrationPlayerAndProfileEndpoints_AreMappedAndRequireTheAdminPolicy()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(new RegistrationsTestHarness().Sender);
        var app = builder.Build();
        app.MapAdminTournamentRegistrationsEndpoints();
        app.MapAdminPlayersEndpoints();
        app.MapAdminEligibilityProfilesEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        const string Registrations = "/api/admin/tournaments/{tournamentId:guid}/registrations";
        const string Players = "/api/admin/organizations/{organizationId:guid}/players";
        const string Profiles = "/api/admin/organizations/{organizationId:guid}/eligibility-profiles";
        Assert.Equal(
            [
                $"DELETE {Profiles}/{{id:guid}}",
                $"GET {Profiles}/",
                $"GET {Profiles}/{{id:guid}}",
                $"GET {Players}/",
                $"GET {Players}/{{id:guid}}",
                $"GET {Registrations}/",
                $"GET {Registrations}/draw-participants",
                $"GET {Registrations}/{{registrationId:guid}}",
                $"POST {Profiles}/",
                $"POST {Players}/",
                $"POST {Registrations}/",
                $"POST {Registrations}/{{registrationId:guid}}/cancel",
                $"POST {Registrations}/{{registrationId:guid}}/confirm",
                $"POST {Registrations}/{{registrationId:guid}}/reject",
                $"POST {Registrations}/{{registrationId:guid}}/waitlist",
                $"PUT {Profiles}/{{id:guid}}",
                $"PUT {Players}/{{id:guid}}",
                $"PUT {Registrations}/{{registrationId:guid}}/payment",
            ],
            endpoints
                .Select(e => $"{e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single()} {e.RoutePattern.RawText}")
                .Order(StringComparer.Ordinal));
        foreach (var endpoint in endpoints)
        {
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), a => a.Policy == "AdminOrAbove");
            Assert.False(string.IsNullOrWhiteSpace(endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary));
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(), p => p.StatusCode == StatusCodes.Status403Forbidden);
        }

        // Every registration write can lose the revision race, so each documents 409 registrations.concurrent_modification.
        foreach (var endpoint in endpoints.Where(e => e.RoutePattern.RawText!.Contains("{registrationId:guid}/", StringComparison.Ordinal)))
        {
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(), p => p.StatusCode == StatusCodes.Status409Conflict);
            Assert.Contains("registrations.concurrent_modification", endpoint.Metadata.GetMetadata<IEndpointDescriptionMetadata>()!.Description, StringComparison.Ordinal);
        }
    }

    private static CreateRegistrationBody Pair(RegistrationsTestHarness h, Guid org, string? status = null) => new(
        [new(1, h.SeedPlayer(org, "Ana", $"Gil {Guid.NewGuid():N}").Id.Value), new(2, h.SeedPlayer(org, "Bea", $"Paz {Guid.NewGuid():N}").Id.Value)],
        status);

    private static Task<IResult> InvokeAsync(
        string operation,
        RegistrationsTestHarness h,
        Guid tournamentId,
        TournamentRegistrationDetails registration,
        int? revision = null)
    {
        var id = registration.Registration.Id.Value;
        var body = new RegistrationRevisionBody(revision);
        return operation switch
        {
            "list" => Endpoints.ListAsync(tournamentId, null, h.Sender, CancellationToken.None),
            "get" => Endpoints.GetAsync(tournamentId, id, h.Sender, CancellationToken.None),
            "create" => Endpoints.CreateAsync(tournamentId, Pair(h, registration.Registration.OrganizationId.Value), h.Sender, CancellationToken.None),
            "confirm" => Endpoints.ConfirmAsync(tournamentId, id, body, h.Sender, CancellationToken.None),
            "reject" => Endpoints.RejectAsync(tournamentId, id, body, h.Sender, CancellationToken.None),
            "waitlist" => Endpoints.WaitlistAsync(tournamentId, id, body, h.Sender, CancellationToken.None),
            "cancel" => Endpoints.CancelAsync(tournamentId, id, body, h.Sender, CancellationToken.None),
            "payment" => Endpoints.SetPaymentAsync(tournamentId, id, new RegistrationPaymentBody("paid", revision), h.Sender, CancellationToken.None),
            _ => Endpoints.DrawParticipantsAsync(tournamentId, h.Sender, CancellationToken.None),
        };
    }

    private static TournamentRegistrationResponse Created(IResult result) =>
        Assert.IsType<Created<TournamentRegistrationResponse>>(result).Value!;

    private static TournamentRegistrationListResponse List(IResult result) =>
        Assert.IsType<Ok<TournamentRegistrationListResponse>>(result).Value!;

    private static void AssertNothingWritten(RegistrationsTestHarness h, TournamentRegistrationDetails registration)
    {
        Assert.Empty(h.Registrations.Writes);
        Assert.Empty(h.Registrations.RejectedWrites);
        var stored = h.Registrations.Find(registration.Registration.Id.Value)!;
        Assert.Equal((registration.Registration.Status, registration.Registration.Revision), (stored.Status, stored.Revision));
    }

    /// <summary>Compares the members by value (records compare lists by reference).</summary>
    private sealed class DrawParticipantComparer : IEqualityComparer<DrawParticipantBody>
    {
        public bool Equals(DrawParticipantBody? x, DrawParticipantBody? y) =>
            x is not null && y is not null
            && (x.Id, x.DisplayName, x.SeedRank, x.SeparationKey) == (y.Id, y.DisplayName, y.SeedRank, y.SeparationKey)
            && x.Members!.SequenceEqual(y.Members!);

        public int GetHashCode(DrawParticipantBody obj) => obj.Id?.GetHashCode(StringComparison.Ordinal) ?? 0;
    }
}
