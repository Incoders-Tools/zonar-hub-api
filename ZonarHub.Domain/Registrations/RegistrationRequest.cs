using ZonarHub.Domain.Common;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Tournaments;

namespace ZonarHub.Domain.Registrations;

/// <summary>
/// A validated new registration (S4). The store decides the final status from <see cref="RequestedStatus"/> and the
/// tournament's places (<see cref="RegistrationRules.Admit"/>) and checks that every player belongs to the tournament's
/// organization and has no other active registration in it.
/// </summary>
public sealed record RegistrationRequest
{
    public const int MaxNotesLength = 2000;

    private RegistrationRequest() { }

    public TournamentId TournamentId { get; private init; }
    public RegistrationSource Source { get; private init; }
    public RegistrationStatus RequestedStatus { get; private init; }
    public RegistrationPaymentStatus PaymentStatus { get; private init; }
    public string? ContactEmail { get; private init; }
    public string? Notes { get; private init; }
    public bool PublicNameConsent { get; private init; }

    /// <summary>Participants ordered by slot number.</summary>
    public IReadOnlyList<RegistrationParticipant> Participants { get; private init; } = [];

    /// <summary>
    /// Validates the input: at least one participant, slot numbers 1..16 without repeats, distinct non-empty players, a
    /// contact email for public submissions, notes up to 2000 characters, and a pending request (or confirmed from the
    /// backoffice).
    /// </summary>
    public static Result<RegistrationRequest> Create(
        TournamentId tournamentId,
        RegistrationSource source,
        RegistrationStatus requestedStatus,
        RegistrationPaymentStatus paymentStatus,
        string? contactEmail,
        string? notes,
        bool publicNameConsent,
        IReadOnlyList<RegistrationParticipant> participants)
    {
        if (participants is null || participants.Count == 0)
            return Result.Failure<RegistrationRequest>(RegistrationErrors.ParticipantsRequired);

        if (participants.Any(p => p.SlotNumber is < 1 or > RegistrationParticipant.MaxSlotNumber)
            || participants.Select(p => p.SlotNumber).Distinct().Count() != participants.Count)
            return Result.Failure<RegistrationRequest>(RegistrationErrors.InvalidSlot);

        if (participants.Any(p => p.PlayerId.Value == Guid.Empty))
            return Result.Failure<RegistrationRequest>(RegistrationErrors.PlayerRequired);

        if (participants.Select(p => p.PlayerId).Distinct().Count() != participants.Count)
            return Result.Failure<RegistrationRequest>(RegistrationErrors.DuplicatePlayer);

        // Only the backoffice confirms on creation; a public submission always waits for the organizer (S4).
        if (requestedStatus != RegistrationStatus.Pending
            && !(requestedStatus == RegistrationStatus.Confirmed && source == RegistrationSource.Admin))
            return Result.Failure<RegistrationRequest>(RegistrationErrors.InvalidTransition);

        var email = Player.NormalizeEmail(contactEmail);
        if (email is null && source == RegistrationSource.Public)
            return Result.Failure<RegistrationRequest>(RegistrationErrors.ContactEmailRequired);

        if (email is not null && !Player.IsValidEmail(email))
            return Result.Failure<RegistrationRequest>(RegistrationErrors.InvalidContactEmail);

        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (trimmedNotes is { Length: > MaxNotesLength })
            return Result.Failure<RegistrationRequest>(RegistrationErrors.NotesTooLong);

        return Result.Success(new RegistrationRequest
        {
            TournamentId = tournamentId,
            Source = source,
            RequestedStatus = requestedStatus,
            PaymentStatus = paymentStatus,
            ContactEmail = email,
            Notes = trimmedNotes,
            PublicNameConsent = publicNameConsent,
            Participants = participants.OrderBy(p => p.SlotNumber).ToList(),
        });
    }
}
