using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.SocialNetworks.GetAll;

public sealed class GetSocialNetworksHandler
    : IRequestHandler<GetSocialNetworksQuery, Result<PageResult<SocialNetworkResponse>>>
{
    private readonly ISocialNetworkRepository _networks;

    public GetSocialNetworksHandler(ISocialNetworkRepository networks)
    {
        _networks = networks;
    }

    public async Task<Result<PageResult<SocialNetworkResponse>>> Handle(
        GetSocialNetworksQuery request,
        CancellationToken cancellationToken)
    {
        var f = Normalize(request.Filter);
        var query = new SocialNetworkQuery(f.NameContains, f.IsActive, f.Page, f.PageSize);
        var (items, total) = await _networks.ListAsync(query, cancellationToken);
        IReadOnlyList<SocialNetworkResponse> mapped = items.Select(SocialNetworkResponse.FromDomain).ToList();

        return Result.Success(new PageResult<SocialNetworkResponse>(mapped, f.Page, f.PageSize, total));
    }

    private static SocialNetworkFilter Normalize(SocialNetworkFilter f)
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
