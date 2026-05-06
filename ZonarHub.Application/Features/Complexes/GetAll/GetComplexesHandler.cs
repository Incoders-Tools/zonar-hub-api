using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Application.Features.Complexes.GetAll;

public sealed class GetComplexesHandler : IRequestHandler<GetComplexesQuery, Result<IReadOnlyList<ComplexResponse>>>
{
    private readonly IComplexRepository _complexes;

    public GetComplexesHandler(IComplexRepository complexes)
    {
        _complexes = complexes;
    }

    public async Task<Result<IReadOnlyList<ComplexResponse>>> Handle(
        GetComplexesQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
            return Result.Failure<IReadOnlyList<ComplexResponse>>(ComplexErrors.OrganizationIdRequired);

        var orgId = new OrganizationId(request.OrganizationId);
        var complexes = await _complexes.ListByOrganizationAsync(orgId, cancellationToken);
        IReadOnlyList<ComplexResponse> response = complexes.Select(ComplexResponse.FromDomain).ToList();

        return Result.Success(response);
    }
}
