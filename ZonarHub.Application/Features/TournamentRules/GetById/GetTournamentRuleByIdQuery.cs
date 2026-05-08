using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentRules.GetById;

public sealed record GetTournamentRuleByIdQuery(Guid Id)
    : IRequest<Result<TournamentRuleResponse>>;
