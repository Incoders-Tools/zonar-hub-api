namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Flat read/write repository for the admin tournaments endpoint.
/// Mirrors every column the admin frontend expects to round-trip;
/// the canonical Tournament domain entity / ITournamentRepository remain
/// dedicated to the simpler use cases (dashboard counters, onboarding).
/// </summary>
public interface ITournamentAdminRepository
{
    Task<IReadOnlyList<TournamentAdminDto>> ListByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<TournamentAdminDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TournamentAdminDto> AddAsync(TournamentAdminDto tournament, CancellationToken cancellationToken = default);

    Task<TournamentAdminDto> UpdateAsync(TournamentAdminDto tournament, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record TournamentAdminDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string? Key,
    Guid? ComplexId,
    Guid SportId,
    Guid? CategoryId,
    Guid? GenderId,
    Guid? ModalityId,
    Guid? TournamentTypeId,
    Guid? RuleSetId,
    string? Status,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly? RegistrationStartDate,
    DateOnly? RegistrationEndDate,
    int? MaxPairs,
    string? Description,
    string? Rules,
    string? ImageUrl,
    string? CoverImageUrl,
    decimal? RegistrationFeePerPair,
    decimal? PrizeMoney,
    int? PointsToAward,
    decimal? SumValue,
    string? Observations,
    bool IsActive,
    IReadOnlyList<Guid> SelectedCourtIds,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
