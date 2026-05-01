namespace ZonarHub.Application.Features.TenantSports;

public sealed record TenantSportEntry(
    Guid SportId,
    string Name,
    string Key,
    string Icon,
    string IconSource,
    int SortOrder,
    bool IsEnabled);

public sealed record TenantSportsResponse(IReadOnlyList<TenantSportEntry> Items);
