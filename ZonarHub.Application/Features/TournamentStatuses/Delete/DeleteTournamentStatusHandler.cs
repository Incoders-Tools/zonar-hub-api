using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.Delete;

public sealed class DeleteTournamentStatusHandler
    : IRequestHandler<DeleteTournamentStatusCommand, Result>
{
    private readonly ISystemSettingRepository _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public DeleteTournamentStatusHandler(
        ISystemSettingRepository settings,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _settings = settings;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(
        DeleteTournamentStatusCommand request,
        CancellationToken cancellationToken)
    {
        var (setting, items) = await TournamentStatusCatalogStore.LoadAsync(_settings, _clock, cancellationToken);
        var removed = items.RemoveAll(item => string.Equals(item.Id, request.Id, StringComparison.OrdinalIgnoreCase));

        if (removed == 0)
        {
            return Result.Failure(TournamentStatusCatalogErrors.NotFound);
        }

        var saved = await TournamentStatusCatalogStore.SaveAsync(
            items,
            setting,
            _settings,
            _unitOfWork,
            _clock,
            cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure(saved.Error);
        }

        return Result.Success();
    }
}
