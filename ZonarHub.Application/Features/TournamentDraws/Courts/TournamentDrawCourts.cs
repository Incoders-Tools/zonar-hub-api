using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Courts;

namespace ZonarHub.Application.Features.TournamentDraws.Courts;

/// <summary>
/// The courts a tournament's matches can be played on (D13): the courts of the tournament's complex, limited to its
/// selected courts when it has any, otherwise to the active ones. A tournament without a complex, or whose complex is
/// missing or belongs to another organization, has none. The same list feeds the result dialog and validates a result's
/// court id.
/// </summary>
internal sealed class TournamentDrawCourts(ICourtRepository courts, IComplexRepository complexes)
{
    /// <summary>Eligible courts of <paramref name="tournament"/>, ordered by name.</summary>
    public async Task<IReadOnlyList<Court>> ListAsync(TournamentAdminDto tournament, CancellationToken cancellationToken)
    {
        if (tournament.ComplexId is not { } complexId)
            return [];

        var complex = await complexes.GetByIdAsync(new ComplexId(complexId), cancellationToken);
        if (complex is null || complex.OrganizationId.Value != tournament.OrganizationId)
            return [];

        var selected = tournament.SelectedCourtIds.ToHashSet();
        var candidates = await courts.ListByComplexIdAsync(complex.Id, cancellationToken);
        return candidates
            .Where(court => selected.Count > 0 ? selected.Contains(court.Id.Value) : court.IsActive)
            .OrderBy(court => court.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The eligible court with <paramref name="courtId"/>; null when it is unknown or not eligible.</summary>
    public async Task<Court?> FindAsync(TournamentAdminDto tournament, Guid courtId, CancellationToken cancellationToken) =>
        (await ListAsync(tournament, cancellationToken)).FirstOrDefault(court => court.Id.Value == courtId);
}
