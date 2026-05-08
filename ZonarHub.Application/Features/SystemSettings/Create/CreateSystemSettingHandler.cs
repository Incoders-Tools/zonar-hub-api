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
        var now = _clock.UtcNow;

        var existing = await _settings.GetByKeyAsync(
            key,
            request.Scope,
            request.TenantId,
            request.UserId,
            cancellationToken);

        // Idempotent create: if a row with the same identity tuple already
        // exists, update its value instead of failing. This handles concurrent
        // POSTs from the frontend (the user-preferences sync fans out 4
        // parallel upserts on every value change) where the find-then-create
        // pattern would otherwise race and a second insert would hit the
        // Supabase UNIQUE(key, scope, tenant_id, user_id) constraint with a
        // 409 — which previously surfaced as a 500 from EnsureSuccessStatusCode.
        if (existing is not null)
        {
            var updateResult = existing.Update(
                request.Value,
                request.Scope,
                request.TenantId,
                request.UserId,
                now);

            if (updateResult.IsFailure)
            {
                return Result.Failure<SystemSettingResponse>(updateResult.Error);
            }

            _settings.Update(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(SystemSettingResponse.FromDomain(existing));
        }

        var created = SystemSetting.Create(
            SystemSettingId.New(),
            key,
            request.Value,
            request.Scope,
            request.TenantId,
            request.UserId,
            now);

        if (created.IsFailure)
        {
            return Result.Failure<SystemSettingResponse>(created.Error);
        }

        await _settings.AddAsync(created.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SystemSettingResponse.FromDomain(created.Value));
    }
}
