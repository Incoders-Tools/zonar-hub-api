using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.Delete;

public sealed class DeleteTournamentStatusHandler
    : IRequestHandler<DeleteTournamentStatusCommand, Result>
{
    private readonly ITournamentStatusRepository _statuses;

    public DeleteTournamentStatusHandler(ITournamentStatusRepository statuses)
    {
        _statuses = statuses;
    }

    public async Task<Result> Handle(
        DeleteTournamentStatusCommand request,
        CancellationToken cancellationToken)
    {
        var current = await _statuses.GetByIdAsync(request.Id, cancellationToken);
        if (current is null)
        {
            return Result.Failure(TournamentStatusCatalogErrors.NotFound);
        }

        await _statuses.DeleteAsync(request.Id, cancellationToken);
        return Result.Success();
    }
}
