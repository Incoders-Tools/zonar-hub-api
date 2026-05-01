using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.SystemSettings;
using MediatR;

namespace ZonarHub.Application.Features.SystemSettings.Create;

public sealed class CreateSystemSettingHandler
    : IRequestHandler<CreateSystemSettingCommand, Result<SystemSettingResponse>>
{
    private readonly ISystemSettingRepository _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateSystemSettingHandler(
        ISystemSettingRepository settings,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _settings = settings;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SystemSettingResponse>> Handle(
        CreateSystemSettingCommand request,
        CancellationToken cancellationToken)
    {
        var key = (request.Key ?? string.Empty).Trim();
        var existing = await _settings.GetByKeyAsync(
            key,
            request.Scope,
            request.TenantId,
            request.UserId,
            cancellationToken);

        if (existing is not null)
        {
            return Result.Failure<SystemSettingResponse>(SystemSettingErrors.KeyAlreadyExists);
        }

        var created = SystemSetting.Create(
            SystemSettingId.New(),
            key,
            request.Value,
            request.Scope,
            request.TenantId,
            request.UserId,
            _clock.UtcNow);

        if (created.IsFailure)
        {
            return Result.Failure<SystemSettingResponse>(created.Error);
        }

        await _settings.AddAsync(created.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SystemSettingResponse.FromDomain(created.Value));
    }
}
