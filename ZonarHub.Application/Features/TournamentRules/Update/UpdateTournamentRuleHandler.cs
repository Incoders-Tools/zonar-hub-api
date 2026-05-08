using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentRules.Update;

public sealed class UpdateTournamentRuleHandler
    : IRequestHandler<UpdateTournamentRuleCommand, Result<TournamentRuleResponse>>
{
    private readonly ITournamentRuleRepository _rules;
    private readonly IClock _clock;

    public UpdateTournamentRuleHandler(ITournamentRuleRepository rules, IClock clock)
    {
        _rules = rules;
        _clock = clock;
    }

    public async Task<Result<TournamentRuleResponse>> Handle(
        UpdateTournamentRuleCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await _rules.GetByIdAsync(request.Id, cancellationToken);
        if (existing is null)
        {
            return Result.Failure<TournamentRuleResponse>(TournamentRuleErrors.NotFound);
        }

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<TournamentRuleResponse>(TournamentRuleErrors.NameRequired);
        }

        var dto = existing with
        {
            Name = name,
            DescriptionEs = NullIfBlank(request.DescriptionEs),
            DescriptionEn = NullIfBlank(request.DescriptionEn),
            DescriptionPt = NullIfBlank(request.DescriptionPt),
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            UpdatedAt = _clock.UtcNow
        };

        var updated = await _rules.UpdateAsync(dto, cancellationToken);
        return Result.Success(TournamentRuleMapper.ToResponse(updated));
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
