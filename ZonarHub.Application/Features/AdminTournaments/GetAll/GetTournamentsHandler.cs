using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminTournaments.GetAll;

public sealed class GetTournamentsHandler
    : IRequestHandler<GetTournamentsQuery, Result<IReadOnlyList<TournamentResponse>>>
{
    private readonly ITournamentAdminRepository _tournaments;

    public GetTournamentsHandler(ITournamentAdminRepository tournaments)
    {
        _tournaments = tournaments;
    }

    public async Task<Result<IReadOnlyList<TournamentResponse>>> Handle(
        GetTournamentsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<IReadOnlyList<TournamentResponse>>(AdminTournamentErrors.OrganizationRequired);
        }

        var rows = await _tournaments.ListByOrganizationAsync(request.OrganizationId, cancellationToken);
        IReadOnlyList<TournamentResponse> mapped = rows.Select(TournamentMapper.ToResponse).ToList();
        return Result.Success(mapped);
    }
}
