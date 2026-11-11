using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Registrations;

/// <summary>
/// D2 status rules. The SQL functions <c>create_tournament_registration</c> and
/// <c>transition_tournament_registration</c> enforce the same table and raise the same <see cref="RegistrationErrors"/>
/// codes under a tournament lock, so a decision taken here on a stale count is re-checked by the store.
/// </summary>
/// <remarks>
/// Transitions: pending → confirmed | rejected | waitlisted | cancelled; waitlisted → confirmed | cancelled;
/// confirmed → cancelled. Rejected and cancelled are final. Confirming needs a free place
/// (<see cref="RegistrationCapacity.HasConfirmedPlace"/>).
/// </remarks>
public static class RegistrationRules
{
    /// <summary>True for every status except the final ones (rejected, cancelled).</summary>
    public static bool IsActive(RegistrationStatus status) =>
        status is not (RegistrationStatus.Rejected or RegistrationStatus.Cancelled);

    /// <summary>True when the D2 table allows moving from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static bool IsAllowedTransition(RegistrationStatus from, RegistrationStatus to) => (from, to) switch
    {
        (RegistrationStatus.Pending, RegistrationStatus.Confirmed or RegistrationStatus.Rejected
            or RegistrationStatus.Waitlisted or RegistrationStatus.Cancelled) => true,
        (RegistrationStatus.Waitlisted, RegistrationStatus.Confirmed or RegistrationStatus.Cancelled) => true,
        (RegistrationStatus.Confirmed, RegistrationStatus.Cancelled) => true,
        _ => false,
    };

    /// <summary>
    /// <see cref="RegistrationErrors.InvalidTransition"/> outside the table, else
    /// <see cref="RegistrationErrors.CapacityReached"/> when confirming without a free place.
    /// </summary>
    public static Result CheckTransition(RegistrationStatus from, RegistrationStatus to, RegistrationCapacity capacity)
    {
        if (!IsAllowedTransition(from, to))
            return Result.Failure(RegistrationErrors.InvalidTransition);

        return to == RegistrationStatus.Confirmed && !capacity.HasConfirmedPlace
            ? Result.Failure(RegistrationErrors.CapacityReached)
            : Result.Success();
    }

    /// <summary>
    /// Status of a new registration: a pending request is waitlisted when confirmed plus pending reach the limit; a
    /// confirmed request (admin only) needs a free place. Any other request is an invalid transition.
    /// </summary>
    public static Result<RegistrationStatus> Admit(RegistrationStatus requested, RegistrationCapacity capacity) => requested switch
    {
        RegistrationStatus.Pending => Result.Success(
            capacity.IsFullForNewEntries ? RegistrationStatus.Waitlisted : RegistrationStatus.Pending),
        RegistrationStatus.Confirmed => capacity.HasConfirmedPlace
            ? Result.Success(RegistrationStatus.Confirmed)
            : Result.Failure<RegistrationStatus>(RegistrationErrors.CapacityReached),
        _ => Result.Failure<RegistrationStatus>(RegistrationErrors.InvalidTransition),
    };

    /// <summary>
    /// A manual payment mark of the backoffice (D5, cash payments): <see cref="RegistrationPaymentStatus.Paid"/> from pending
    /// or failed, <see cref="RegistrationPaymentStatus.Refunded"/> from paid. Other targets are
    /// <see cref="RegistrationErrors.InvalidPaymentStatus"/>, other origins <see cref="RegistrationErrors.InvalidPaymentTransition"/>.
    /// </summary>
    public static Result CheckManualPayment(RegistrationPaymentStatus from, RegistrationPaymentStatus to) => to switch
    {
        RegistrationPaymentStatus.Paid when from is RegistrationPaymentStatus.Pending or RegistrationPaymentStatus.Failed => Result.Success(),
        RegistrationPaymentStatus.Refunded when from == RegistrationPaymentStatus.Paid => Result.Success(),
        RegistrationPaymentStatus.Paid or RegistrationPaymentStatus.Refunded => Result.Failure(RegistrationErrors.InvalidPaymentTransition),
        _ => Result.Failure(RegistrationErrors.InvalidPaymentStatus),
    };

    /// <summary>Payment status of a new registration: pending when the fee per pair is positive, else not required.</summary>
    public static RegistrationPaymentStatus InitialPaymentStatus(decimal? feePerPair) =>
        feePerPair is > 0 ? RegistrationPaymentStatus.Pending : RegistrationPaymentStatus.NotRequired;
}
