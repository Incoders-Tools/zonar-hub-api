using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Sports;
using MediatR;

namespace ZonarHub.Application.Features.Sports.GetById;

public sealed class GetSportByIdHandler : IRequestHandler<GetSportByIdQuery, Result<SportResponse>>
{
    private readonly ISportRepository _sports;

    public GetSportByIdHandler(ISportRepository sports)
    {
        _sports = sports;
    }

    public async Task<Result<SportResponse>> Handle(
        GetSportByIdQuery request,
        CancellationToken cancellationToken)
    {
        var sport = await _sports.GetByIdAsync(new SportId(request.Id), cancellationToken);
        if (sport is null)
        {
            return Result.Failure<SportResponse>(SportErrors.NotFound);
        }

        return Result.Success(SportResponse.FromDomain(sport));
    }
}
