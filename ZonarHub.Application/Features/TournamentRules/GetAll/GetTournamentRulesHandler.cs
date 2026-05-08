using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentRules.GetAll;

public sealed class GetTournamentRulesHandler
    : IRequestHandler<GetTournamentRulesQuery, Result<IReadOnlyList<TournamentRuleResponse>>>
{
    private readonly ITournamentRuleRepository _rules;

    public GetTournamentRulesHandler(ITournamentRuleRepository rules)
    {
        _rules = rules;
    }

    public async Task<Result<IReadOnlyList<TournamentRuleResponse>>> Handle(
        GetTournamentRulesQuery request,
        CancellationToken cancellationToken)
    {
        var items = await _rules.GetAllAsync(request.IncludeInactive, cancellationToken);
        IReadOnlyList<TournamentRuleResponse> mapped = items.Select(TournamentRuleMapper.ToResponse).ToList();
        return Result.Success(mapped);
    }
}
