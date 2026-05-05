using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.GetAll;

public sealed class GetTournamentStatusesHandler
    : IRequestHandler<GetTournamentStatusesQuery, Result<IReadOnlyList<TournamentStatusResponse>>>
{
    private readonly ISystemSettingRepository _settings;
    private readonly IClock _clock;

    public GetTournamentStatusesHandler(
        ISystemSettingRepository settings,
        IClock clock)
    {
        _settings = settings;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<TournamentStatusResponse>>> Handle(
        GetTournamentStatusesQuery request,
        CancellationToken cancellationToken)
    {
        var (_, items) = await TournamentStatusCatalogStore.LoadAsync(_settings, _clock, cancellationToken);
        IReadOnlyList<TournamentStatusResponse> mapped = items.Select(item => item.ToResponse()).ToList();
        return Result.Success(mapped);
    }
}
