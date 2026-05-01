using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Organizations.GetAll;

public sealed class GetOrganizationsHandler
    : IRequestHandler<GetOrganizationsQuery, Result<PageResult<OrganizationResponse>>>
{
    private readonly IOrganizationRepository _organizations;

    public GetOrganizationsHandler(IOrganizationRepository organizations)
    {
        _organizations = organizations;
    }

    public async Task<Result<PageResult<OrganizationResponse>>> Handle(
        GetOrganizationsQuery request,
        CancellationToken cancellationToken)
    {
        var f = Normalize(request.Filter);
        var query = new OrganizationQuery(
            f.TenantId,
            f.DisplayNameContains,
            f.Type,
            f.IsActive,
            f.Page,
            f.PageSize);

        var (items, total) = await _organizations.ListAsync(query, cancellationToken);
        IReadOnlyList<OrganizationResponse> mapped = items
            .Select(OrganizationResponse.FromDomain)
            .ToList();

        return Result.Success(new PageResult<OrganizationResponse>(mapped, f.Page, f.PageSize, total));
    }

    private static OrganizationFilter Normalize(OrganizationFilter f)
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
