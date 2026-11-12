using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Registrations;

/// <summary>
/// Registration errors. Codes are stable: the store refusals (<see cref="FromStoreCode"/>) are the exact messages the
/// SQL functions of migration <c>20261110100000_tournament_registrations</c> raise with SQLSTATE <c>PT409</c>.
/// </summary>
public static class RegistrationErrors
{
    public static readonly Error NotFound = Error.NotFound("registrations.not_found", "registrations.errors.not_found");
    public static readonly Error CapacityReached = Error.Conflict("registrations.capacity_reached", "registrations.errors.capacity_reached");
    public static readonly Error PlayerAlreadyRegistered = Error.Conflict("registrations.player_already_registered", "registrations.errors.player_already_registered");
    public static readonly Error ConcurrentModification = Error.Conflict("registrations.concurrent_modification", "registrations.errors.concurrent_modification");
    public static readonly Error InvalidTransition = Error.Conflict("registrations.invalid_transition", "registrations.errors.invalid_transition");
    public static readonly Error TournamentNotFound = Error.NotFound("registrations.tournament_not_found", "registrations.errors.tournament_not_found");
    public static readonly Error PlayerNotFound = Error.NotFound("registrations.player_not_found", "registrations.errors.player_not_found");
    public static readonly Error ParticipantsRequired = Error.Validation("registrations.participants_required", "registrations.errors.participants_required");
    public static readonly Error InvalidSlot = Error.Validation("registrations.invalid_slot", "registrations.errors.invalid_slot");
    public static readonly Error DuplicatePlayer = Error.Validation("registrations.duplicate_player", "registrations.errors.duplicate_player");
    public static readonly Error PlayerRequired = Error.Validation("registrations.player_required", "registrations.errors.player_required");
    public static readonly Error ContactEmailRequired = Error.Validation("registrations.contact_email_required", "registrations.errors.contact_email_required");
    public static readonly Error InvalidContactEmail = Error.Validation("registrations.invalid_contact_email", "registrations.errors.invalid_contact_email");
    public static readonly Error NotesTooLong = Error.Validation("registrations.notes_too_long", "registrations.errors.notes_too_long");

    // D3 eligibility (RegistrationEligibility).
    public static readonly Error RequiredSlotMissing = Error.Validation("registrations.required_slot_missing", "registrations.errors.required_slot_missing");
    public static readonly Error GenderMismatch = Error.Validation("registrations.gender_mismatch", "registrations.errors.gender_mismatch");
    public static readonly Error CategoryOutOfRange = Error.Validation("registrations.category_out_of_range", "registrations.errors.category_out_of_range");
    public static readonly Error AgeOutOfRange = Error.Validation("registrations.age_out_of_range", "registrations.errors.age_out_of_range");

    /// <summary>A status the request may not ask for (unknown, or not pending/confirmed on creation).</summary>
    public static readonly Error InvalidStatus = Error.Validation("registrations.invalid_status", "registrations.errors.invalid_status");

    /// <summary>A payment status the backoffice may not mark (only paid and refunded).</summary>
    public static readonly Error InvalidPaymentStatus = Error.Validation("registrations.invalid_payment_status", "registrations.errors.invalid_payment_status");

    /// <summary>The current payment status does not allow the mark (paid needs pending or failed, refunded needs paid).</summary>
    public static readonly Error InvalidPaymentTransition = Error.Conflict("registrations.invalid_payment_transition", "registrations.errors.invalid_payment_transition");

    private static readonly IReadOnlyDictionary<string, Error> StoreRefusals = new[]
    {
        CapacityReached, PlayerAlreadyRegistered, ConcurrentModification, InvalidTransition, TournamentNotFound, PlayerNotFound,
    }.ToDictionary(error => error.Code, StringComparer.Ordinal);

    /// <summary>The error of a store refusal message; null for any other text.</summary>
    public static Error? FromStoreCode(string? code) =>
        code is not null && StoreRefusals.TryGetValue(code, out var error) ? error : null;
}
