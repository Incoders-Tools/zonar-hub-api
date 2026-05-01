namespace ZonarHub.Application.Features.OrganizationSports;

public sealed record OrganizationSportEntry(
    Guid SportId,
    string Name,
    string Key,
    string Icon,
    string IconSource,
    int SortOrder,
    bool IsEnabled);

public sealed record OrganizationSportsResponse(IReadOnlyList<OrganizationSportEntry> Items);
