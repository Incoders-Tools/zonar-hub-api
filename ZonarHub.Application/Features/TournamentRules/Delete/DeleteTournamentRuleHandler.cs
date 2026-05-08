using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentRules.Delete;

public sealed class DeleteTournamentRuleHandler : IRequestHandler<DeleteTournamentRuleCommand, Result>
{
    private readonly ITournamentRuleRepository _rules;

    public DeleteTournamentRuleHandler(ITournamentRuleRepository rules)
    {
        _rules = rules;
    }

    public async Task<Result> Handle(DeleteTournamentRuleCommand request, CancellationToken cancellationToken)
    {
        var existing = await _rules.GetByIdAsync(request.Id, cancellationToken);
        if (existing is null)
        {
            return Result.Failure(TournamentRuleErrors.NotFound);
        }

        await _rules.DeleteAsync(request.Id, cancellationToken);
        return Result.Success();
    }
}
