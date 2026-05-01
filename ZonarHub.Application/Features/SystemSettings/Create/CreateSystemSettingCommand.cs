using ZonarHub.Application.Features.SystemSettings;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.SystemSettings;
using MediatR;

namespace ZonarHub.Application.Features.SystemSettings.Create;

public sealed record CreateSystemSettingCommand(
    string Key,
    string Value,
    SystemSettingScope Scope,
    Guid? TenantId,
    Guid? UserId) : IRequest<Result<SystemSettingResponse>>;
