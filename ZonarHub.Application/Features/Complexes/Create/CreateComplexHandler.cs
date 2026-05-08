using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Application.Features.Complexes.Create;

public sealed class CreateComplexHandler : IRequestHandler<CreateComplexCommand, Result<ComplexResponse>>
{
    private readonly IComplexRepository _complexes;
    private readonly IUnitOfWork _unitOfWork;

    public CreateComplexHandler(IComplexRepository complexes, IUnitOfWork unitOfWork)
    {
        _complexes = complexes;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ComplexResponse>> Handle(
        CreateComplexCommand request,
        CancellationToken cancellationToken)
    {
        var result = Complex.Create(
            ComplexId.New(),
            new OrganizationId(request.OrganizationId),
            request.Name,
            request.Key,
            request.Address,
            request.Location,
            request.Description,
            request.SortOrder,
            request.Preponderance,
            request.LogoImagePath,
            request.CoverImagePath,
            request.LayoutDiagramPath,
            DateTime.UtcNow);

        if (result.IsFailure)
            return Result.Failure<ComplexResponse>(result.Error);

        await _complexes.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ComplexResponse.FromDomain(result.Value));
    }
}
