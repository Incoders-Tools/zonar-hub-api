namespace ZonarHub.Application.Features.TournamentStatuses;

public sealed record TournamentStatusResponse(
    string Id,
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
