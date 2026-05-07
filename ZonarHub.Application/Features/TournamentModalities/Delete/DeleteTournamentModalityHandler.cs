using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentModalities.Delete;

public sealed class DeleteTournamentModalityHandler : IRequestHandler<DeleteTournamentModalityCommand, Result>
{
    private readonly ITournamentModalityRepository _modalities;

    public DeleteTournamentModalityHandler(ITournamentModalityRepository modalities)
    {
        _modalities = modalities;
    }

    public async Task<Result> Handle(DeleteTournamentModalityCommand request, CancellationToken cancellationToken)
    {
        var current = await _modalities.GetByIdAsync(request.Id, cancellationToken);
        if (current is null)
        {
            return Result.Failure(TournamentModalityErrors.NotFound);
        }

        await _modalities.DeleteAsync(request.Id, cancellationToken);
        return Result.Success();
    }
}
