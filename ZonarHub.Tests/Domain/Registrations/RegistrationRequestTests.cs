using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Registrations;
using ZonarHub.Domain.Tournaments;

namespace ZonarHub.Tests.Domain.Registrations;

/// <summary>Input rules of a new tournament registration (S4, D1) before the store decides its status.</summary>
public class RegistrationRequestTests
{
    private static readonly TournamentId Tournament = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"));
    private static readonly PlayerId PlayerA = new(Guid.Parse("00000000-0000-0000-0000-0000000000f1"));
    private static readonly PlayerId PlayerB = new(Guid.Parse("00000000-0000-0000-0000-0000000000f2"));

    [Fact]
    public void Create_NormalizesTheInput_AndOrdersParticipantsBySlot()
    {
        var result = RegistrationRequest.Create(
            Tournament,
            RegistrationSource.Public,
            RegistrationStatus.Pending,
            RegistrationPaymentStatus.Pending,
            "  Pair@Example.COM ",
            "  bring rackets  ",
            publicNameConsent: true,
            [new RegistrationParticipant(2, PlayerB), new RegistrationParticipant(1, PlayerA)]);

        var request = result.Value;
        Assert.Equal(Tournament, request.TournamentId);
        Assert.Equal(RegistrationSource.Public, request.Source);
        Assert.Equal(RegistrationStatus.Pending, request.RequestedStatus);
        Assert.Equal(RegistrationPaymentStatus.Pending, request.PaymentStatus);
        Assert.Equal("pair@example.com", request.ContactEmail);
        Assert.Equal("bring rackets", request.Notes);
        Assert.True(request.PublicNameConsent);
        Assert.Equal([1, 2], request.Participants.Select(p => p.SlotNumber));
        Assert.Equal([PlayerA, PlayerB], request.Participants.Select(p => p.PlayerId));
    }

    [Fact]
    public void Create_AdminWithoutContactOrNotes_KeepsThemNull_AndMayRequestConfirmed()
    {
        var request = Create(RegistrationSource.Admin, RegistrationStatus.Confirmed, contactEmail: "  ", notes: " ").Value;

        Assert.Null(request.ContactEmail);
        Assert.Null(request.Notes);
        Assert.Equal(RegistrationStatus.Confirmed, request.RequestedStatus);
    }

    [Fact]
    public void Create_PublicWithoutContactEmail_IsRefused()
    {
        Assert.Equal("registrations.contact_email_required", Create(RegistrationSource.Public, contactEmail: null).Error.Code);
        Assert.Equal("registrations.contact_email_required", Create(RegistrationSource.Public, contactEmail: "  ").Error.Code);
    }

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("@example.com")]
    [InlineData("pair@")]
    [InlineData("two words@example.com")]
    [InlineData("a@b@c")]
    public void Create_InvalidContactEmail_IsRefused(string email)
    {
        Assert.Equal("registrations.invalid_contact_email", Create(RegistrationSource.Admin, contactEmail: email).Error.Code);
    }

    [Fact]
    public void Create_ContactEmailLongerThan254_IsRefused()
    {
        var email = new string('a', 243) + "@example.com";

        Assert.Equal(255, email.Length);
        Assert.Equal("registrations.invalid_contact_email", Create(RegistrationSource.Admin, contactEmail: email).Error.Code);
        Assert.True(Create(RegistrationSource.Admin, contactEmail: email[1..]).IsSuccess);
    }

    [Fact]
    public void Create_NotesLongerThan2000_AreRefused()
    {
        Assert.True(Create(RegistrationSource.Admin, notes: new string('n', 2000)).IsSuccess);
        Assert.Equal("registrations.notes_too_long", Create(RegistrationSource.Admin, notes: new string('n', 2001)).Error.Code);
    }

    [Theory]
    [InlineData(RegistrationStatus.Waitlisted)]
    [InlineData(RegistrationStatus.Rejected)]
    [InlineData(RegistrationStatus.Cancelled)]
    public void Create_RequestedStatusOtherThanPendingOrConfirmed_IsAnInvalidTransition(RegistrationStatus requested)
    {
        Assert.Equal("registrations.invalid_transition", Create(RegistrationSource.Admin, requested).Error.Code);
    }

    [Fact]
    public void Create_PublicRequestingConfirmed_IsAnInvalidTransition()
    {
        Assert.Equal("registrations.invalid_transition", Create(RegistrationSource.Public, RegistrationStatus.Confirmed).Error.Code);
    }

    [Fact]
    public void Create_WithoutParticipants_IsRefused()
    {
        Assert.Equal("registrations.participants_required", Create(participants: []).Error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(17)]
    public void Create_SlotOutsideOneToSixteen_IsRefused(int slot)
    {
        Assert.Equal("registrations.invalid_slot", Create(participants: [new RegistrationParticipant(slot, PlayerA)]).Error.Code);
    }

    [Fact]
    public void Create_SixteenSlots_IsTheMaximum()
    {
        var participants = Enumerable.Range(1, 16).Select(slot => new RegistrationParticipant(slot, PlayerId.New())).ToList();

        Assert.Equal(16, Create(participants: participants).Value.Participants.Count);
    }

    [Fact]
    public void Create_DuplicateSlot_IsRefused()
    {
        var result = Create(participants: [new RegistrationParticipant(1, PlayerA), new RegistrationParticipant(1, PlayerB)]);

        Assert.Equal("registrations.invalid_slot", result.Error.Code);
    }

    [Fact]
    public void Create_SamePlayerTwice_IsRefused()
    {
        var result = Create(participants: [new RegistrationParticipant(1, PlayerA), new RegistrationParticipant(2, PlayerA)]);

        Assert.Equal("registrations.duplicate_player", result.Error.Code);
    }

    [Fact]
    public void Create_EmptyPlayerId_IsRefused()
    {
        var result = Create(participants: [new RegistrationParticipant(1, new PlayerId(Guid.Empty))]);

        Assert.Equal("registrations.player_required", result.Error.Code);
    }

    [Fact]
    public void Create_NullParticipants_IsRefused()
    {
        var result = RegistrationRequest.Create(
            Tournament, RegistrationSource.Admin, RegistrationStatus.Pending, RegistrationPaymentStatus.NotRequired,
            null, null, publicNameConsent: false, participants: null!);

        Assert.Equal("registrations.participants_required", result.Error.Code);
    }

    [Fact]
    public void TournamentRegistration_ExposesItsRulesThroughTheEntity()
    {
        var registration = TournamentRegistration.Reconstitute(
            new TournamentRegistrationId(Guid.Parse("00000000-0000-0000-0000-0000000000e1")),
            Tournament,
            new OrganizationId(Guid.Parse("00000000-0000-0000-0000-0000000000b1")),
            RegistrationStatus.Waitlisted,
            RegistrationSource.Public,
            RegistrationPaymentStatus.Pending,
            "pair@example.com",
            null,
            publicNameConsent: false,
            [new RegistrationParticipant(2, PlayerB), new RegistrationParticipant(1, PlayerA)],
            revision: 3,
            new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc));

        Assert.True(registration.IsActive);
        Assert.Equal([1, 2], registration.Participants.Select(p => p.SlotNumber));
        Assert.Equal(3, registration.Revision);
        Assert.True(registration.CheckTransition(RegistrationStatus.Confirmed, new RegistrationCapacity(4, 3, 0)).IsSuccess);
        Assert.Equal(
            "registrations.capacity_reached",
            registration.CheckTransition(RegistrationStatus.Confirmed, new RegistrationCapacity(4, 4, 0)).Error.Code);
        Assert.Equal(
            "registrations.invalid_transition",
            registration.CheckTransition(RegistrationStatus.Rejected, new RegistrationCapacity(4, 0, 0)).Error.Code);
    }

    private static ZonarHub.Domain.Common.Result<RegistrationRequest> Create(
        RegistrationSource source = RegistrationSource.Admin,
        RegistrationStatus requested = RegistrationStatus.Pending,
        string? contactEmail = "pair@example.com",
        string? notes = null,
        IReadOnlyList<RegistrationParticipant>? participants = null) =>
        RegistrationRequest.Create(
            Tournament,
            source,
            requested,
            RegistrationPaymentStatus.NotRequired,
            contactEmail,
            notes,
            publicNameConsent: false,
            participants ?? [new RegistrationParticipant(1, PlayerA), new RegistrationParticipant(2, PlayerB)]);
}
