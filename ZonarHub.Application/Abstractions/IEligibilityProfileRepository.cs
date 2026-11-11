using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Registrations;

namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Store of the organization-scoped eligibility profiles and their slots (D1). A profile of another organization is
/// reported as missing. Writes are immediate (no unit of work); a save writes the profile and all its slots atomically.
/// </summary>
public interface IEligibilityProfileRepository
{
    /// <summary>The organization's profiles with their slots, ordered by name.</summary>
    Task<IReadOnlyList<EligibilityProfile>> ListByOrganizationAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>The organization's profile with its slots, or null.</summary>
    Task<EligibilityProfile?> GetByIdAsync(
        OrganizationId organizationId,
        EligibilityProfileId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The <c>eligibility_profile_id</c> of the tournament, or null when it has none or does not exist. Callers load the
    /// profile with <see cref="GetByIdAsync"/> under the tournament's organization.
    /// </summary>
    Task<EligibilityProfileId?> GetTournamentProfileIdAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>Inserts the profile, or replaces its columns and every slot; returns it as stored.</summary>
    /// <exception cref="PersistenceConflictException">
    /// <see cref="EligibilityProfileErrors.NameTaken"/> when another profile of the organization has the name (any case),
    /// <see cref="EligibilityProfileErrors.NotFound"/> when the id belongs to another organization.
    /// </exception>
    Task<EligibilityProfile> SaveAsync(EligibilityProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Deletes the organization's profile with its slots; false when there was none.</summary>
    /// <exception cref="PersistenceConflictException">
    /// <see cref="EligibilityProfileErrors.InUse"/> when a tournament references the profile.
    /// </exception>
    Task<bool> DeleteAsync(OrganizationId organizationId, EligibilityProfileId id, CancellationToken cancellationToken = default);
}
