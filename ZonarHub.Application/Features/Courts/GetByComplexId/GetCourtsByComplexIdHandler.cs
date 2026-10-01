using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Courts.GetAll;
using ZonarHub.Domain.Complexes;
using MediatR;

namespace ZonarHub.Application.Features.Courts.GetByComplexId;

internal sealed class GetCourtsByComplexIdHandler : IRequestHandler<GetCourtsByComplexIdQuery, IReadOnlyList<CourtDto>>
{
    private readonly ICourtRepository _courts;

    public GetCourtsByComplexIdHandler(ICourtRepository courts)
    {
        _courts = courts;
    }

    public async Task<IReadOnlyList<CourtDto>> Handle(GetCourtsByComplexIdQuery request, CancellationToken cancellationToken)
    {
        var courts = await _courts.ListByComplexIdAsync(new ComplexId(request.ComplexId), cancellationToken);

        return courts.Select(c => new CourtDto(
            c.Id.Value,
            c.ComplexId.Value,
            c.Name,
            c.IsActive,
            c.CreatedAtUtc, c.IsIndoor, c.SurfaceType, c.SportIds))
            .ToList();
    }
}
