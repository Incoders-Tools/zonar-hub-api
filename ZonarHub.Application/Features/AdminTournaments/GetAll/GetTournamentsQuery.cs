using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminTournaments.GetAll;

public sealed record GetTournamentsQuery(Guid OrganizationId)
    : IRequest<Result<IReadOnlyList<TournamentResponse>>>;
