namespace ZonarHub.Application.Features.TournamentModalities;

public sealed record TournamentModalityResponse(
    Guid Id,
    string NameEs,
    string NameEn,
    string NamePt,
    string Key,
    int SortOrder,
    bool IsActive);

public sealed record TournamentModalityListResponse(IReadOnlyList<TournamentModalityResponse> Items);
