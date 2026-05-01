namespace ZonarHub.Application.Common.Pagination;

/// <summary>
/// Transport-neutral pagination + sort request used by list queries.
/// </summary>
public sealed record PageRequest(
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Ascending)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;

    public PageRequest Normalize() => new(
        Page < 1 ? DefaultPage : Page,
        PageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => PageSize,
        },
        string.IsNullOrWhiteSpace(SortBy) ? null : SortBy.Trim(),
        SortDirection);

    public int Skip => (Page - 1) * PageSize;
}
