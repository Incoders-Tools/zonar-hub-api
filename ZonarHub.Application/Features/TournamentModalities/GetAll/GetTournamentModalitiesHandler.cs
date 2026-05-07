using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.TournamentModalities;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentModalities.GetAll;

public sealed class GetTournamentModalitiesHandler
    : IRequestHandler<GetTournamentModalitiesQuery, Result<IReadOnlyList<TournamentModalityResponse>>>
{
    private readonly ITournamentModalityRepository _modalities;

    public GetTournamentModalitiesHandler(ITournamentModalityRepository modalities)
    {
        _modalities = modalities;
    }

    public async Task<Result<IReadOnlyList<TournamentModalityResponse>>> Handle(
        GetTournamentModalitiesQuery request,
        CancellationToken cancellationToken)
    {
        var items = await _modalities.GetAllAsync(request.IncludeInactive, cancellationToken);

        IReadOnlyList<TournamentModalityResponse> result = items
            .Select(m => new TournamentModalityResponse(m.Id, m.NameEs, m.NameEn, m.NamePt, m.Key, m.SortOrder, m.IsActive))
            .ToList();

        return Result.Success(result);
    }
}
