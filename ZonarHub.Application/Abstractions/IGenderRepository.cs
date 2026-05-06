namespace ZonarHub.Application.Abstractions;

public interface IGenderRepository
{
    Task<IReadOnlyList<GenderDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<GenderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<GenderDto> AddAsync(GenderDto gender, CancellationToken cancellationToken = default);
    Task<GenderDto?> UpdateAsync(Guid id, GenderDto gender, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record GenderDto(
    Guid Id,
    string Name,
    string Key,
    bool IsActive,
    int SortOrder,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
