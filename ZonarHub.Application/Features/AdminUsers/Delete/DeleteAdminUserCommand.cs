using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminUsers.Delete;

public sealed record DeleteAdminUserCommand(Guid UserId) : IRequest<Result>;
