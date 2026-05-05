using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.GetById;

public sealed class GetTournamentStatusByIdHandler
    : IRequestHandler<GetTournamentStatusByIdQuery, Result<TournamentStatusResponse>>
{
    private readonly ISystemSettingRepository _settings;
    private readonly IClock _clock;

    public GetTournamentStatusByIdHandler(
        ISystemSettingRepository settings,
        IClock clock)
    {
        _settings = settings;
        _clock = clock;
    }

    public async Task<Result<TournamentStatusResponse>> Handle(
        GetTournamentStatusByIdQuery request,
        CancellationToken cancellationToken)
    {
        var (_, items) = await TournamentStatusCatalogStore.LoadAsync(_settings, _clock, cancellationToken);
        var status = items.FirstOrDefault(item => string.Equals(item.Id, request.Id, StringComparison.OrdinalIgnoreCase));
        if (status is null)
        {
            return Result.Failure<TournamentStatusResponse>(TournamentStatusCatalogErrors.NotFound);
        }

        return Result.Success(status.ToResponse());
    }
}
