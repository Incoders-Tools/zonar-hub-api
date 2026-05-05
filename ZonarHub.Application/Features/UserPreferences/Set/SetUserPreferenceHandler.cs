using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.SystemSettings;
using MediatR;

namespace ZonarHub.Application.Features.UserPreferences.Set;

public sealed class SetUserPreferenceHandler
    : IRequestHandler<SetUserPreferenceCommand, Result>
{
    private readonly ISystemSettingRepository _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public SetUserPreferenceHandler(
        ISystemSettingRepository settings,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _settings = settings;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(
        SetUserPreferenceCommand request,
        CancellationToken cancellationToken)
    {
        var key = (request.Key ?? string.Empty).Trim();
        var existing = await _settings.GetByKeyAsync(
            key,
            SystemSettingScope.User,
            request.OrganizationId,
            request.UserId,
            cancellationToken);

        if (existing is not null)
        {
            var updateResult = existing.Update(
                request.Value,
                SystemSettingScope.User,
                request.OrganizationId,
                request.UserId,
                _clock.UtcNow);

            if (updateResult.IsFailure)
            {
                return updateResult;
            }

            _settings.Update(existing);
        }
        else
        {
            var created = SystemSetting.Create(
                SystemSettingId.New(),
                key,
                request.Value,
                SystemSettingScope.User,
                request.OrganizationId,
                request.UserId,
                _clock.UtcNow);

            if (created.IsFailure)
            {
                return Result.Failure(created.Error);
            }

            await _settings.AddAsync(created.Value, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
