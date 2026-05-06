namespace ZonarHub.Application.Abstractions;

public interface ICategoryRepository
{
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CategoryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CategoryDto> AddAsync(CategoryDto category, CancellationToken cancellationToken = default);
    Task<CategoryDto?> UpdateAsync(Guid id, CategoryDto category, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record CategoryDto(
    Guid Id,
    string Name,
    string ShortName,
    string Key,
    int Level,
    bool IsActive,
    int SortOrder,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
