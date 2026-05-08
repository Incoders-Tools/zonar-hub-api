namespace ZonarHub.Application.Features.TournamentRules;

public sealed record TournamentRuleResponse(
    Guid Id,
    string Name,
    string? DescriptionEs,
    string? DescriptionEn,
    string? DescriptionPt,
    int SortOrder,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);
