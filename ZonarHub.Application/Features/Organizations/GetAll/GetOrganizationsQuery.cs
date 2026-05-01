using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.Organizations;
using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Organizations.GetAll;

public sealed record GetOrganizationsQuery(OrganizationFilter Filter)
    : IRequest<Result<PageResult<OrganizationResponse>>>;

public sealed record OrganizationFilter(
    Guid? TenantId = null,
    string? DisplayNameContains = null,
    string? Type = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20);
