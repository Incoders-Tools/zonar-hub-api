using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminTournaments.GetById;

public sealed class GetTournamentByIdHandler
    : IRequestHandler<GetTournamentByIdQuery, Result<TournamentResponse>>
{
    private readonly ITournamentAdminRepository _tournaments;

    public GetTournamentByIdHandler(ITournamentAdminRepository tournaments)
    {
        _tournaments = tournaments;
    }

    public async Task<Result<TournamentResponse>> Handle(
        GetTournamentByIdQuery request,
        CancellationToken cancellationToken)
    {
        var current = await _tournaments.GetByIdAsync(request.Id, cancellationToken);
        if (current is null)
        {
            return Result.Failure<TournamentResponse>(AdminTournamentErrors.NotFound);
        }

        return Result.Success(TournamentMapper.ToResponse(current));
    }
}
