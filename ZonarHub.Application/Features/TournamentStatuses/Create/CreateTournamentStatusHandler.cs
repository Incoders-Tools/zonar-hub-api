using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.Create;

public sealed class CreateTournamentStatusHandler
    : IRequestHandler<CreateTournamentStatusCommand, Result<TournamentStatusResponse>>
{
    private readonly ISystemSettingRepository _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateTournamentStatusHandler(
        ISystemSettingRepository settings,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _settings = settings;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<TournamentStatusResponse>> Handle(
        CreateTournamentStatusCommand request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<TournamentStatusResponse>(TournamentStatusCatalogErrors.NameRequired);
        }

        var key = request.Key.Trim().ToLowerInvariant();

        var (setting, items) = await TournamentStatusCatalogStore.LoadAsync(_settings, _clock, cancellationToken);

        if (items.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<TournamentStatusResponse>(TournamentStatusCatalogErrors.NameAlreadyExists);
        }

        if (items.Any(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<TournamentStatusResponse>(TournamentStatusCatalogErrors.KeyAlreadyExists);
        }

        var nowUtc = _clock.UtcNow;
        var created = new TournamentStatusCatalogItem(
            Id: $"ts_{Guid.NewGuid():N}",
            Name: name,
            Key: key,
            Description: string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            SortOrder: request.SortOrder,
            IsActive: true,
            CreatedAt: nowUtc,
            UpdatedAt: nowUtc);

        items.Add(created);

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

        return Result.Success(created.ToResponse());
    }
}
