using ZonarHub.Application.Common.Pagination;
using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Sports.GetAll;

public sealed record GetSportsQuery(SportFilter Filter)
    : IRequest<Result<PageResult<SportResponse>>>;

public sealed record SportFilter(
    string? NameContains = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20);
