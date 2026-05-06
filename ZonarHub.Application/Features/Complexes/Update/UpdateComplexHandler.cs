using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Complexes;

namespace ZonarHub.Application.Features.Complexes.Update;

public sealed class UpdateComplexHandler : IRequestHandler<UpdateComplexCommand, Result<ComplexResponse>>
{
    private readonly IComplexRepository _complexes;

    public UpdateComplexHandler(IComplexRepository complexes)
    {
        _complexes = complexes;
    }

    public async Task<Result<ComplexResponse>> Handle(
        UpdateComplexCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await _complexes.GetByIdAsync(new ComplexId(request.Id), cancellationToken);
        if (existing is null)
            return Result.Failure<ComplexResponse>(ComplexErrors.NotFound);

        static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        var updated = Complex.Reconstitute(
            existing.Id,
            existing.OrganizationId,
            request.Name,
            NormalizeOptional(request.Key),
            request.Address,
            NormalizeOptional(request.Location),
            NormalizeOptional(request.Description),
            request.SortOrder,
            request.Preponderance,
            NormalizeOptional(request.LogoImagePath),
            NormalizeOptional(request.CoverImagePath),
            NormalizeOptional(request.LayoutDiagramPath),
            request.IsActive,
            existing.CreatedAtUtc,
            DateTime.UtcNow);

        await _complexes.UpdateAsync(updated, cancellationToken);

        return Result.Success(ComplexResponse.FromDomain(updated));
    }
}
