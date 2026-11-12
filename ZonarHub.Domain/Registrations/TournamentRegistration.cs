using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Tournaments;

namespace ZonarHub.Domain.Registrations;

/// <summary>
/// A stored registration of a pair (or of the modality's participants) in a tournament (S4). Writes go through the store
/// functions that apply <see cref="RegistrationRules"/> under a tournament lock and bump <see cref="Revision"/>.
/// </summary>
public sealed class TournamentRegistration : Entity<TournamentRegistrationId>
{
    private TournamentRegistration(TournamentRegistrationId id) : base(id) { }

    public TournamentId TournamentId { get; private set; }
    public OrganizationId OrganizationId { get; private set; }
    public RegistrationStatus Status { get; private set; }
    public RegistrationSource Source { get; private set; }
    public RegistrationPaymentStatus PaymentStatus { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? Notes { get; private set; }
    public bool PublicNameConsent { get; private set; }

    /// <summary>Participants ordered by slot number.</summary>
    public IReadOnlyList<RegistrationParticipant> Participants { get; private set; } = [];

    /// <summary>Incremented by every write; the expected value of the next one (optimistic concurrency).</summary>
    public int Revision { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Not rejected nor cancelled: its players cannot join another registration of the tournament.</summary>
    public bool IsActive => RegistrationRules.IsActive(Status);

    public static TournamentRegistration Reconstitute(
        TournamentRegistrationId id,
        TournamentId tournamentId,
        OrganizationId organizationId,
        RegistrationStatus status,
        RegistrationSource source,
        RegistrationPaymentStatus paymentStatus,
        string? contactEmail,
        string? notes,
        bool publicNameConsent,
        IReadOnlyList<RegistrationParticipant> participants,
        int revision,
        DateTime createdAtUtc,
        DateTime updatedAtUtc) => new(id)
    {
        TournamentId = tournamentId,
        OrganizationId = organizationId,
        Status = status,
        Source = source,
        PaymentStatus = paymentStatus,
        ContactEmail = contactEmail,
        Notes = notes,
        PublicNameConsent = publicNameConsent,
        Participants = participants.OrderBy(p => p.SlotNumber).ToList(),
        Revision = revision,
        CreatedAtUtc = createdAtUtc,
        UpdatedAtUtc = updatedAtUtc,
    };

    /// <summary>Whether this registration may move to <paramref name="target"/> (<see cref="RegistrationRules.CheckTransition"/>).</summary>
    public Result CheckTransition(RegistrationStatus target, RegistrationCapacity capacity) =>
        RegistrationRules.CheckTransition(Status, target, capacity);
}
