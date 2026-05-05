using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.Update;

public sealed class UpdateTournamentStatusHandler
    : IRequestHandler<UpdateTournamentStatusCommand, Result<TournamentStatusResponse>>
{
    private readonly ISystemSettingRepository _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpdateTournamentStatusHandler(
        ISystemSettingRepository settings,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _settings = settings;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<TournamentStatusResponse>> Handle(
        UpdateTournamentStatusCommand request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<TournamentStatusResponse>(TournamentStatusCatalogErrors.NameRequired);
        }

        var (setting, items) = await TournamentStatusCatalogStore.LoadAsync(_settings, _clock, cancellationToken);
        var index = items.FindIndex(item => string.Equals(item.Id, request.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return Result.Failure<TournamentStatusResponse>(TournamentStatusCatalogErrors.NotFound);
        }

        var current = items[index];

        var duplicateName = items.Any(item =>
            !string.Equals(item.Id, current.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

        if (duplicateName)
        {
            return Result.Failure<TournamentStatusResponse>(TournamentStatusCatalogErrors.NameAlreadyExists);
        }

        var updated = current with
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            UpdatedAt = _clock.UtcNow
        };

        items[index] = updated;

        var saved = await TournamentStatusCatalogStore.SaveAsync(
            items,
            setting,
            _settings,
            _unitOfWork,
            _clock,
            cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<TournamentStatusResponse>(saved.Error);
        }

        return Result.Success(updated.ToResponse());
    }
}
