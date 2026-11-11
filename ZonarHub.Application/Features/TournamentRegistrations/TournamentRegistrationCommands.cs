using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Registrations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.TournamentRegistrations;

/// <summary>Confirms, rejects, waitlists or cancels a registration (D2).</summary>
/// <param name="ExpectedRevision">The revision the client read, or null to act on the current one.</param>
public sealed record TransitionTournamentRegistrationCommand(
    Guid TournamentId,
    Guid RegistrationId,
    RegistrationStatus Target,
    int? ExpectedRevision) : IRequest<Result<TournamentRegistrationResponse>>;

/// <summary>Marks a manual payment (D5): <c>paid</c> for a cash payment, <c>refunded</c> when it is returned.</summary>
public sealed record SetTournamentRegistrationPaymentCommand(
    Guid TournamentId,
    Guid RegistrationId,
    string? PaymentStatus,
    int? ExpectedRevision) : IRequest<Result<TournamentRegistrationResponse>>;

/// <remarks>
/// Tenant rule; <see cref="RegistrationErrors.NotFound"/> for a registration of another tournament; then, without
/// writing, <see cref="RegistrationErrors.ConcurrentModification"/> when <c>ExpectedRevision</c> is stale and the D2
/// check (<see cref="RegistrationErrors.InvalidTransition"/>, <see cref="RegistrationErrors.CapacityReached"/>). The write
/// expects the revision that was read, so a change committed meanwhile is refused by the store with the same errors.
/// </remarks>
public sealed class TransitionTournamentRegistrationHandler
    : IRequestHandler<TransitionTournamentRegistrationCommand, Result<TournamentRegistrationResponse>>
{
    private readonly TournamentDrawScope _scope;
    private readonly ITournamentRegistrationRepository _registrations;

    public TransitionTournamentRegistrationHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments,
        ITournamentRegistrationRepository registrations)
    {
        _scope = new TournamentDrawScope(currentUser, users, organizations, tournaments);
        _registrations = registrations;
    }

    public async Task<Result<TournamentRegistrationResponse>> Handle(
        TransitionTournamentRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var tournament = await _scope.LoadTournamentAsync(request.TournamentId, cancellationToken);
        if (tournament.IsFailure)
            return Result.Failure<TournamentRegistrationResponse>(tournament.Error);

        var current = await LoadCurrentAsync(_registrations, request.TournamentId, request.RegistrationId, request.ExpectedRevision, cancellationToken);
        if (current.IsFailure)
            return Result.Failure<TournamentRegistrationResponse>(current.Error);

        var confirmed = request.Target == RegistrationStatus.Confirmed
            ? await _registrations.CountAsync(request.TournamentId, [RegistrationStatus.Confirmed], cancellationToken)
            : 0;
        var check = current.Value.Registration.CheckTransition(
            request.Target, new RegistrationCapacity(tournament.Value.MaxPairs, confirmed, 0));
        if (check.IsFailure)
            return Result.Failure<TournamentRegistrationResponse>(check.Error);

        var written = await PersistenceWrites.TryAsync(
            () => _registrations.TransitionAsync(current.Value.Version, request.Target, cancellationToken));
        return written.IsFailure
            ? Result.Failure<TournamentRegistrationResponse>(written.Error)
            : Result.Success(TournamentRegistrationMapper.ToResponse(written.Value));
    }

    /// <summary>The tournament's registration, when <paramref name="expectedRevision"/> (if any) is still current.</summary>
    internal static async Task<Result<TournamentRegistrationDetails>> LoadCurrentAsync(
        ITournamentRegistrationRepository registrations,
        Guid tournamentId,
        Guid registrationId,
        int? expectedRevision,
        CancellationToken cancellationToken)
    {
        var details = await registrations.GetAsync(tournamentId, new TournamentRegistrationId(registrationId), cancellationToken);
        if (details is null)
            return Result.Failure<TournamentRegistrationDetails>(RegistrationErrors.NotFound);

        return expectedRevision is { } revision && revision != details.Registration.Revision
            ? Result.Failure<TournamentRegistrationDetails>(RegistrationErrors.ConcurrentModification)
            : Result.Success(details);
    }
}

/// <remarks>
/// Tenant rule; <see cref="RegistrationErrors.InvalidPaymentStatus"/> unless <c>paid</c> or <c>refunded</c>; the
/// registration and revision as in <see cref="TransitionTournamentRegistrationHandler"/>; then
/// <see cref="RegistrationRules.CheckManualPayment"/> (409 <see cref="RegistrationErrors.InvalidPaymentTransition"/>). Any
/// registration status may be marked (a cancelled registration may still be refunded).
/// </remarks>
public sealed class SetTournamentRegistrationPaymentHandler
    : IRequestHandler<SetTournamentRegistrationPaymentCommand, Result<TournamentRegistrationResponse>>
{
    private readonly TournamentDrawScope _scope;
    private readonly ITournamentRegistrationRepository _registrations;

    public SetTournamentRegistrationPaymentHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments,
        ITournamentRegistrationRepository registrations)
    {
        _scope = new TournamentDrawScope(currentUser, users, organizations, tournaments);
        _registrations = registrations;
    }

    public async Task<Result<TournamentRegistrationResponse>> Handle(
        SetTournamentRegistrationPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var tournament = await _scope.LoadTournamentAsync(request.TournamentId, cancellationToken);
        if (tournament.IsFailure)
            return Result.Failure<TournamentRegistrationResponse>(tournament.Error);

        if (!RegistrationApiNames.TryParse(request.PaymentStatus, out RegistrationPaymentStatus target)
            || target is not (RegistrationPaymentStatus.Paid or RegistrationPaymentStatus.Refunded))
            return Result.Failure<TournamentRegistrationResponse>(RegistrationErrors.InvalidPaymentStatus);

        var current = await TransitionTournamentRegistrationHandler.LoadCurrentAsync(
            _registrations, request.TournamentId, request.RegistrationId, request.ExpectedRevision, cancellationToken);
        if (current.IsFailure)
            return Result.Failure<TournamentRegistrationResponse>(current.Error);

        var check = RegistrationRules.CheckManualPayment(current.Value.Registration.PaymentStatus, target);
        if (check.IsFailure)
            return Result.Failure<TournamentRegistrationResponse>(check.Error);

        var written = await PersistenceWrites.TryAsync(
            () => _registrations.SetPaymentStatusAsync(current.Value.Version, target, cancellationToken));
        return written.IsFailure
            ? Result.Failure<TournamentRegistrationResponse>(written.Error)
            : Result.Success(TournamentRegistrationMapper.ToResponse(written.Value));
    }
}
