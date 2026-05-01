using ZonarHub.Domain.Sports;

namespace ZonarHub.Application.Features.Sports;

public sealed record SportResponse(
    Guid Id,
    string Name,
    string Key,
    string Icon,
    string IconSource,
    IReadOnlyList<Guid> ModalityIds,
    bool IsActive,
    int SortOrder,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static SportResponse FromDomain(Sport sport) =>
        new(
            sport.Id.Value,
            sport.Name,
            sport.Key,
            sport.Icon,
            sport.IconSource.ToString().ToLowerInvariant(),
            sport.ModalityIds,
            sport.IsActive,
            sport.SortOrder,
            sport.CreatedAtUtc,
            sport.UpdatedAtUtc);
}
