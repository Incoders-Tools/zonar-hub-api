using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.GetAll;

public sealed class GetTournamentStatusesHandler
    : IRequestHandler<GetTournamentStatusesQuery, Result<IReadOnlyList<TournamentStatusResponse>>>
{
    private readonly ITournamentStatusRepository _statuses;

    public GetTournamentStatusesHandler(ITournamentStatusRepository statuses)
    {
        _statuses = statuses;
    }

    public async Task<Result<IReadOnlyList<TournamentStatusResponse>>> Handle(
        GetTournamentStatusesQuery request,
        CancellationToken cancellationToken)
    {
        var items = await _statuses.GetAllAsync(request.IncludeInactive, cancellationToken);
        IReadOnlyList<TournamentStatusResponse> mapped = items.Select(TournamentStatusMapper.ToResponse).ToList();
        return Result.Success(mapped);
    }
}
