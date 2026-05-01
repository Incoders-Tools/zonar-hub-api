using ZonarHub.Application.Common.Pagination;
using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.SocialNetworks.GetAll;

public sealed record GetSocialNetworksQuery(SocialNetworkFilter Filter)
    : IRequest<Result<PageResult<SocialNetworkResponse>>>;

public sealed record SocialNetworkFilter(
    string? NameContains = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20);
