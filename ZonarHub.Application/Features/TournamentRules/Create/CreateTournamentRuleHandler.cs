using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentRules.Create;

public sealed class CreateTournamentRuleHandler
    : IRequestHandler<CreateTournamentRuleCommand, Result<TournamentRuleResponse>>
{
    private readonly ITournamentRuleRepository _rules;
    private readonly IClock _clock;

    public CreateTournamentRuleHandler(ITournamentRuleRepository rules, IClock clock)
    {
        _rules = rules;
        _clock = clock;
    }

    public async Task<Result<TournamentRuleResponse>> Handle(
        CreateTournamentRuleCommand request,
        CancellationToken cancellationToken)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<TournamentRuleResponse>(TournamentRuleErrors.NameRequired);
        }

        var nowUtc = _clock.UtcNow;
        var dto = new TournamentRuleDto(
            Id: Guid.NewGuid(),
            Name: name,
            DescriptionEs: NullIfBlank(request.DescriptionEs),
            DescriptionEn: NullIfBlank(request.DescriptionEn),
            DescriptionPt: NullIfBlank(request.DescriptionPt),
            SortOrder: request.SortOrder,
            IsActive: true,
            CreatedAt: nowUtc,
            UpdatedAt: nowUtc);

        var inserted = await _rules.AddAsync(dto, cancellationToken);
        return Result.Success(TournamentRuleMapper.ToResponse(inserted));
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
