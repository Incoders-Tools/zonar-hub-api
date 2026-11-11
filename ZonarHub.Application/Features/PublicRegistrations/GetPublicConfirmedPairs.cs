using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.TournamentRegistrations;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Registrations;

namespace ZonarHub.Application.Features.PublicRegistrations;

/// <summary>The confirmed registrations of a showcase tournament, for its public page (S4, D4).</summary>
public sealed record GetPublicConfirmedPairsQuery(string Slug) : IRequest<Result<PublicConfirmedPairsResponse>>;

/// <remarks>
/// Only member names leave the API, and only for registrations with <c>public_name_consent</c>; the others are listed
/// with <see cref="PublicRegistrationKeys.AnonymousPair"/>. No registration or player ids, emails, phones or documents.
/// </remarks>
public sealed class GetPublicConfirmedPairsHandler
    : IRequestHandler<GetPublicConfirmedPairsQuery, Result<PublicConfirmedPairsResponse>>
{
    private readonly PublicRegistrationScope _scope;
    private readonly ITournamentRegistrationRepository _registrations;

    public GetPublicConfirmedPairsHandler(
        IPublicTournamentReadRepository showcase,
        ITournamentAdminRepository tournaments,
        ITournamentRegistrationRepository registrations)
    {
        _scope = new PublicRegistrationScope(showcase, tournaments);
        _registrations = registrations;
    }

    public async Task<Result<PublicConfirmedPairsResponse>> Handle(GetPublicConfirmedPairsQuery request, CancellationToken cancellationToken)
    {
        var loaded = await _scope.LoadAsync(request.Slug, cancellationToken);
        if (loaded.IsFailure)
            return Result.Failure<PublicConfirmedPairsResponse>(loaded.Error);

        var confirmed = await _registrations.ListByTournamentAsync(loaded.Value.Id, RegistrationStatus.Confirmed, cancellationToken);
        return Result.Success(new PublicConfirmedPairsResponse(confirmed.Select((details, index) =>
        {
            if (!details.Registration.PublicNameConsent)
                return new PublicConfirmedPairResponse(index + 1, null, [], PublicRegistrationKeys.AnonymousPair);

            var members = details.Participants.OrderBy(p => p.SlotNumber).Select(p => p.DisplayName).ToList();
            return new PublicConfirmedPairResponse(
                index + 1, string.Join(TournamentRegistrationMapper.MemberSeparator, members), members, null);
        }).ToList()));
    }
}
