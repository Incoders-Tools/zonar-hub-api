using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.GetById;

public sealed class GetTournamentStatusByIdHandler
    : IRequestHandler<GetTournamentStatusByIdQuery, Result<TournamentStatusResponse>>
{
    private readonly ITournamentStatusRepository _statuses;

    public GetTournamentStatusByIdHandler(ITournamentStatusRepository statuses)
    {
        _statuses = statuses;
    }

    public async Task<Result<TournamentStatusResponse>> Handle(
        GetTournamentStatusByIdQuery request,
        CancellationToken cancellationToken)
    {
        var current = await _statuses.GetByIdAsync(request.Id, cancellationToken);
        if (current is null)
        {
            return Result.Failure<TournamentStatusResponse>(TournamentStatusCatalogErrors.NotFound);
        }

        return Result.Success(TournamentStatusMapper.ToResponse(current));
    }
}
