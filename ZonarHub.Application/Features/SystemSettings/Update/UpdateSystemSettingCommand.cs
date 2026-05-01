using ZonarHub.Application.Features.SystemSettings;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.SystemSettings;
using MediatR;

namespace ZonarHub.Application.Features.SystemSettings.Update;

public sealed record UpdateSystemSettingCommand(
    Guid Id,
    string Value,
    SystemSettingScope Scope,
    Guid? TenantId,
    Guid? UserId,
    Guid? RequiredTenantId = null) : IRequest<Result<SystemSettingResponse>>;
