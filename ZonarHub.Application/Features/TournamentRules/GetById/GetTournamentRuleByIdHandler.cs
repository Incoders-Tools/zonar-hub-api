using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentRules.GetById;

public sealed class GetTournamentRuleByIdHandler
    : IRequestHandler<GetTournamentRuleByIdQuery, Result<TournamentRuleResponse>>
{
    private readonly ITournamentRuleRepository _rules;

    public GetTournamentRuleByIdHandler(ITournamentRuleRepository rules)
    {
        _rules = rules;
    }

    public async Task<Result<TournamentRuleResponse>> Handle(
        GetTournamentRuleByIdQuery request,
        CancellationToken cancellationToken)
    {
        var rule = await _rules.GetByIdAsync(request.Id, cancellationToken);
        return rule is null
            ? Result.Failure<TournamentRuleResponse>(TournamentRuleErrors.NotFound)
            : Result.Success(TournamentRuleMapper.ToResponse(rule));
    }
}
