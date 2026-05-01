using ZonarHub.Application.Features.SystemSettings;
using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.SystemSettings.GetById;

public sealed record GetSystemSettingByIdQuery(Guid Id, Guid? RequiredTenantId = null)
    : IRequest<Result<SystemSettingResponse>>;
