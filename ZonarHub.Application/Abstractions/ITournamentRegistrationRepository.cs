using ZonarHub.Domain.Players;
using ZonarHub.Domain.Registrations;

namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Store of tournament registrations and their participants (S4, D1, D2).
/// </summary>
/// <remarks>
/// Every write is one store call and one transaction that locks the tournament, re-applies the D2 rules
/// (<see cref="RegistrationRules"/>) against the current counts and, for existing registrations, checks the expected
/// <see cref="TournamentRegistrationVersion"/>. A refused write throws <see cref="PersistenceConflictException"/> with
/// one of the store errors of <see cref="RegistrationErrors.FromStoreCode"/> and writes nothing. Callers may validate
/// with the domain first for a precise error, but the store decision is the authoritative one (counts may have changed).
/// Reads do not filter by organization: the caller resolves the tournament and applies the tenant rule first.
/// </remarks>
public interface ITournamentRegistrationRepository
{
    /// <summary>
    /// The tournament's registrations ordered by creation time (then id), each with its participants and player summaries;
    /// only those in <paramref name="status"/> when given.
    /// </summary>
    Task<IReadOnlyList<TournamentRegistrationDetails>> ListByTournamentAsync(
        Guid tournamentId,
        RegistrationStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>The tournament's registration with its participants, or null (also when it belongs to another tournament).</summary>
    Task<TournamentRegistrationDetails?> GetAsync(
        Guid tournamentId,
        TournamentRegistrationId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the registration at revision 0 with the status <see cref="RegistrationRules.Admit"/> decides from the
    /// current counts (pending may become waitlisted). The organization is the tournament's.
    /// </summary>
    /// <exception cref="PersistenceConflictException">
    /// <see cref="RegistrationErrors.TournamentNotFound"/>, <see cref="RegistrationErrors.PlayerNotFound"/> (a player of
    /// another organization), <see cref="RegistrationErrors.PlayerAlreadyRegistered"/>, <see cref="RegistrationErrors.CapacityReached"/>
    /// (confirmed request without a place).
    /// </exception>
    Task<TournamentRegistrationDetails> CreateAsync(RegistrationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Moves the registration at <paramref name="expected"/> to <paramref name="status"/> at the next revision.</summary>
    /// <exception cref="PersistenceConflictException">
    /// <see cref="RegistrationErrors.ConcurrentModification"/> (not at <paramref name="expected"/> or deleted),
    /// <see cref="RegistrationErrors.InvalidTransition"/>, <see cref="RegistrationErrors.CapacityReached"/>.
    /// </exception>
    Task<TournamentRegistrationDetails> TransitionAsync(
        TournamentRegistrationVersion expected,
        RegistrationStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>Sets the payment status of the registration at <paramref name="expected"/> at the next revision.</summary>
    /// <exception cref="PersistenceConflictException">
    /// <see cref="RegistrationErrors.ConcurrentModification"/> (not at <paramref name="expected"/> or deleted).
    /// </exception>
    Task<TournamentRegistrationDetails> SetPaymentStatusAsync(
        TournamentRegistrationVersion expected,
        RegistrationPaymentStatus paymentStatus,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Number of the tournament's registrations in any of <paramref name="statuses"/> (all statuses when empty), e.g.
    /// confirmed and pending for a <see cref="RegistrationCapacity"/>.
    /// </summary>
    Task<int> CountAsync(
        Guid tournamentId,
        IReadOnlyCollection<RegistrationStatus> statuses,
        CancellationToken cancellationToken = default);
}

/// <summary>A stored registration at one revision: what a write expects to still be current.</summary>
public sealed record TournamentRegistrationVersion(TournamentRegistrationId RegistrationId, int Revision);

/// <summary>A registration with its participants' player summaries, ordered by slot.</summary>
public sealed record TournamentRegistrationDetails(
    TournamentRegistration Registration,
    IReadOnlyList<RegisteredParticipant> Participants)
{
    /// <summary>The version a write of this registration expects.</summary>
    public TournamentRegistrationVersion Version => new(Registration.Id, Registration.Revision);
}

/// <summary>A participant slot with the player data the admin lists, draw participants and eligibility checks use.</summary>
public sealed record RegisteredParticipant(
    int SlotNumber,
    PlayerId PlayerId,
    string FirstName,
    string LastName,
    string? Club,
    Guid? GenderId,
    Guid? CategoryId,
    DateOnly? BirthDate)
{
    /// <summary>First and last name separated by a space (as <see cref="Player.DisplayName"/>).</summary>
    public string DisplayName => $"{FirstName} {LastName}";
}
