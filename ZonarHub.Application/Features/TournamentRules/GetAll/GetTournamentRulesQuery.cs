using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentRules.GetAll;

public sealed record GetTournamentRulesQuery(bool IncludeInactive = true)
    : IRequest<Result<IReadOnlyList<TournamentRuleResponse>>>;
