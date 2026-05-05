using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.GetById;

public sealed record GetTournamentStatusByIdQuery(string Id)
    : IRequest<Result<TournamentStatusResponse>>;
