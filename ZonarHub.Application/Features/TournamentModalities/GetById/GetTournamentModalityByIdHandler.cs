using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentModalities.GetById;

public sealed class GetTournamentModalityByIdHandler
    : IRequestHandler<GetTournamentModalityByIdQuery, Result<TournamentModalityResponse>>
{
    private readonly ITournamentModalityRepository _modalities;

    public GetTournamentModalityByIdHandler(ITournamentModalityRepository modalities)
    {
        _modalities = modalities;
    }

    public async Task<Result<TournamentModalityResponse>> Handle(
        GetTournamentModalityByIdQuery request,
        CancellationToken cancellationToken)
    {
        var current = await _modalities.GetByIdAsync(request.Id, cancellationToken);
        if (current is null)
        {
            return Result.Failure<TournamentModalityResponse>(TournamentModalityErrors.NotFound);
        }

        return Result.Success(new TournamentModalityResponse(
            current.Id, current.NameEs, current.NameEn, current.NamePt, current.Key, current.SortOrder, current.IsActive));
    }
}
