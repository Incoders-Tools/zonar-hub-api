namespace ZonarHub.Application.Abstractions;

public interface ITournamentModalityRepository
{
    Task<IReadOnlyList<TournamentModalityDto>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TournamentModalityDto>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    Task<TournamentModalityDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TournamentModalityDto?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<TournamentModalityDto> AddAsync(TournamentModalityDto modality, CancellationToken cancellationToken = default);

    Task<TournamentModalityDto> UpdateAsync(TournamentModalityDto modality, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record TournamentModalityDto(
    Guid Id,
    string NameEs,
    string NameEn,
    string NamePt,
    string Key,
    int SortOrder,
    bool IsActive);
