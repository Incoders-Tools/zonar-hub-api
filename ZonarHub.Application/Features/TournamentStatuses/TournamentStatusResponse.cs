namespace ZonarHub.Application.Features.TournamentStatuses;

public sealed record TournamentStatusResponse(
    string Id,
    string Name,
    string Key,
    string? Description,
    int SortOrder,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);
