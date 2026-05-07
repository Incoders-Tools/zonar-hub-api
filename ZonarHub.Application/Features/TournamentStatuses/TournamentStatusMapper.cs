using ZonarHub.Application.Abstractions;

namespace ZonarHub.Application.Features.TournamentStatuses;

internal static class TournamentStatusMapper
{
    public static TournamentStatusResponse ToResponse(TournamentStatusDto dto) => new(
        dto.Id.ToString(),
        dto.Key,
        dto.NameEs,
        dto.NameEn,
        dto.NamePt,
        dto.DescriptionEs,
        dto.DescriptionEn,
        dto.DescriptionPt,
        dto.SortOrder,
        dto.IsActive,
        dto.CreatedAt,
        dto.UpdatedAt);
}
