using ZonarHub.Application.Abstractions;

namespace ZonarHub.Application.Features.TournamentRules;

internal static class TournamentRuleMapper
{
    public static TournamentRuleResponse ToResponse(TournamentRuleDto dto) => new(
        dto.Id,
        dto.Name,
        dto.DescriptionEs,
        dto.DescriptionEn,
        dto.DescriptionPt,
        dto.SortOrder,
        dto.IsActive,
        dto.CreatedAt,
        dto.UpdatedAt);
}
