using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminRoles.Delete;

public sealed record DeleteAdminRoleCommand(string Id) : IRequest<Result>;
