using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Courts.GetAll;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Courts;
using MediatR;

namespace ZonarHub.Application.Features.Courts.GetById;

internal sealed class GetCourtByIdHandler : IRequestHandler<GetCourtByIdQuery, Result<CourtDto>>
{
    private readonly ICourtRepository _courts;

    public GetCourtByIdHandler(ICourtRepository courts)
    {
        _courts = courts;
    }

    public async Task<Result<CourtDto>> Handle(GetCourtByIdQuery request, CancellationToken cancellationToken)
    {
        var court = await _courts.GetByIdAsync(new CourtId(request.Id), cancellationToken);
        if (court is null)
        {
            return Result.Failure<CourtDto>(CourtErrors.NotFound);
        }

        var dto = new CourtDto(
            court.Id.Value,
            court.ComplexId.Value,
            court.Name,
            court.IsActive,
            court.CreatedAtUtc, court.IsIndoor, court.SurfaceType, court.SportIds);

        return Result.Success(dto);
    }
}
