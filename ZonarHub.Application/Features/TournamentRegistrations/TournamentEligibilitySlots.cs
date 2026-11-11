using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Registrations;

namespace ZonarHub.Application.Features.TournamentRegistrations;

/// <summary>
/// The participant slots a tournament's registrations are checked against (D3), shared by the backoffice and the public
/// registration: the slots of the tournament's active eligibility profile, else those of its modality, gender and
/// category (<see cref="RegistrationEligibility.TournamentSlots"/>). An inactive profile is ignored (L8).
/// </summary>
internal sealed class TournamentEligibilitySlots(IEligibilityProfileRepository profiles, ITournamentModalityRepository modalities)
{
    /// <summary>
    /// A referenced profile the tournament's organization does not have is <see cref="EligibilityProfileErrors.NotFound"/>:
    /// the rules are unknown, so nothing is admitted.
    /// </summary>
    public async Task<Result<IReadOnlyList<EligibilitySlot>>> ResolveAsync(TournamentAdminDto tournament, CancellationToken cancellationToken)
    {
        if (await profiles.GetTournamentProfileIdAsync(tournament.Id, cancellationToken) is { } profileId)
        {
            var profile = await profiles.GetByIdAsync(new OrganizationId(tournament.OrganizationId), profileId, cancellationToken);
            if (profile is null)
                return Result.Failure<IReadOnlyList<EligibilitySlot>>(EligibilityProfileErrors.NotFound);

            if (profile.IsActive)
                return Result.Success(profile.Slots);
        }

        var modality = tournament.ModalityId is { } modalityId
            ? await modalities.GetByIdAsync(modalityId, cancellationToken)
            : null;
        return Result.Success(RegistrationEligibility.TournamentSlots(modality?.Key, tournament.GenderId, tournament.CategoryId));
    }
}
