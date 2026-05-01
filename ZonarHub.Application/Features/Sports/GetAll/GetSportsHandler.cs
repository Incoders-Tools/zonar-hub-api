using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Sports.GetAll;

public sealed class GetSportsHandler : IRequestHandler<GetSportsQuery, Result<PageResult<SportResponse>>>
{
    private readonly ISportRepository _sports;

    public GetSportsHandler(ISportRepository sports)
    {
        _sports = sports;
    }

    public async Task<Result<PageResult<SportResponse>>> Handle(
        GetSportsQuery request,
        CancellationToken cancellationToken)
    {
        var f = Normalize(request.Filter);
        var query = new SportQuery(f.NameContains, f.IsActive, f.Page, f.PageSize);
        var (items, total) = await _sports.ListAsync(query, cancellationToken);
        IReadOnlyList<SportResponse> mapped = items.Select(SportResponse.FromDomain).ToList();

        return Result.Success(new PageResult<SportResponse>(mapped, f.Page, f.PageSize, total));
    }

    private static SportFilter Normalize(SportFilter f)
    {
        var page = f.Page < 1 ? PageRequest.DefaultPage : f.Page;
        var pageSize = f.PageSize switch
        {
            < 1 => PageRequest.DefaultPageSize,
            > PageRequest.MaxPageSize => PageRequest.MaxPageSize,
            _ => f.PageSize,
        };
        return f with { Page = page, PageSize = pageSize };
    }
}
