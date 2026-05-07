using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.GetAll;

public sealed record GetTournamentStatusesQuery(bool IncludeInactive = true)
    : IRequest<Result<IReadOnlyList<TournamentStatusResponse>>>;
