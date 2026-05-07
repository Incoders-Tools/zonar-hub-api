namespace ZonarHub.Application.Abstractions;

public interface ITournamentStatusRepository
{
    Task<IReadOnlyList<TournamentStatusDto>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default);

    Task<TournamentStatusDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TournamentStatusDto?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<TournamentStatusDto> AddAsync(TournamentStatusDto status, CancellationToken cancellationToken = default);

    Task<TournamentStatusDto> UpdateAsync(TournamentStatusDto status, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record TournamentStatusDto(
    Guid Id,
    string Key,
    string NameEs,
    string NameEn,
    string NamePt,
    string? DescriptionEs,
    string? DescriptionEn,
    string? DescriptionPt,
    int SortOrder,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);
