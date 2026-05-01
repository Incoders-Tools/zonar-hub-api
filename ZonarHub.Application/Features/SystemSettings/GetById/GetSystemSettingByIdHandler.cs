using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.SystemSettings;
using MediatR;

namespace ZonarHub.Application.Features.SystemSettings.GetById;

public sealed class GetSystemSettingByIdHandler
    : IRequestHandler<GetSystemSettingByIdQuery, Result<SystemSettingResponse>>
{
    private readonly ISystemSettingRepository _settings;

    public GetSystemSettingByIdHandler(ISystemSettingRepository settings)
    {
        _settings = settings;
    }

    public async Task<Result<SystemSettingResponse>> Handle(
        GetSystemSettingByIdQuery request,
        CancellationToken cancellationToken)
    {
        var setting = await _settings.GetByIdAsync(new SystemSettingId(request.Id), cancellationToken);
        if (setting is null)
        {
            return Result.Failure<SystemSettingResponse>(SystemSettingErrors.NotFound);
        }

        if (request.RequiredTenantId is { } tenantId
            && setting.Scope != SystemSettingScope.Global
            && setting.TenantId != tenantId)
        {
            return Result.Failure<SystemSettingResponse>(SystemSettingErrors.CrossTenantAccessDenied);
        }

        return Result.Success(SystemSettingResponse.FromDomain(setting));
    }
}
