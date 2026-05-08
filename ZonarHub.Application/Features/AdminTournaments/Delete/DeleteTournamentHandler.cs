using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminTournaments.Delete;

public sealed class DeleteTournamentHandler : IRequestHandler<DeleteTournamentCommand, Result>
{
    private readonly ITournamentAdminRepository _tournaments;

    public DeleteTournamentHandler(ITournamentAdminRepository tournaments)
    {
        _tournaments = tournaments;
    }

    public async Task<Result> Handle(DeleteTournamentCommand request, CancellationToken cancellationToken)
    {
        var current = await _tournaments.GetByIdAsync(request.Id, cancellationToken);
        if (current is null)
        {
            return Result.Failure(AdminTournamentErrors.NotFound);
        }

        await _tournaments.DeleteAsync(request.Id, cancellationToken);
        return Result.Success();
    }
}
