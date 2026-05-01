using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.SystemSettings.Delete;

public sealed record DeleteSystemSettingCommand(Guid Id, Guid? RequiredTenantId = null) : IRequest<Result>;
