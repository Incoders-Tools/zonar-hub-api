using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.SystemSettings;
using MediatR;

namespace ZonarHub.Application.Features.SystemSettings.Update;

public sealed class UpdateSystemSettingHandler
    : IRequestHandler<UpdateSystemSettingCommand, Result<SystemSettingResponse>>
{
    private readonly ISystemSettingRepository _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpdateSystemSettingHandler(
        ISystemSettingRepository settings,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _settings = settings;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SystemSettingResponse>> Handle(
        UpdateSystemSettingCommand request,
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

        var updated = setting.Update(
            request.Value,
            request.Scope,
            request.TenantId,
            request.UserId,
            _clock.UtcNow);

        if (updated.IsFailure)
        {
            return Result.Failure<SystemSettingResponse>(updated.Error);
        }

        _settings.Update(setting);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SystemSettingResponse.FromDomain(setting));
    }
}
