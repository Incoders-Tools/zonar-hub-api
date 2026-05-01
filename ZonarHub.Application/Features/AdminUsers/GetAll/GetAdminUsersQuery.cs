using MediatR;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminUsers.GetAll;

public sealed record GetAdminUsersQuery(AdminUserFilter Filter)
    : IRequest<Result<PageResult<AdminUserResponse>>>;
