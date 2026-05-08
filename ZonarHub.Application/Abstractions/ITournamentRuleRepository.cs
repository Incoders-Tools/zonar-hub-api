namespace ZonarHub.Application.Abstractions;

public interface ITournamentRuleRepository
{
    Task<IReadOnlyList<TournamentRuleDto>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default);

    Task<TournamentRuleDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TournamentRuleDto> AddAsync(TournamentRuleDto rule, CancellationToken cancellationToken = default);

    Task<TournamentRuleDto> UpdateAsync(TournamentRuleDto rule, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record TournamentRuleDto(
    Guid Id,
    string Name,
    string? DescriptionEs,
    string? DescriptionEn,
    string? DescriptionPt,
    int SortOrder,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);
