using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Courts;
using MediatR;

namespace ZonarHub.Application.Features.Courts.Create;

internal sealed class CreateCourtHandler : IRequestHandler<CreateCourtCommand, Result<Guid>>
{
    private readonly ICourtRepository _courts;
    private readonly IComplexRepository _complexes;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateCourtHandler(
        ICourtRepository courts,
        IComplexRepository complexes,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _courts = courts;
        _complexes = complexes;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<Guid>> Handle(CreateCourtCommand request, CancellationToken cancellationToken)
    {
        var complexId = new ComplexId(request.ComplexId);

        var complex = await _complexes.GetByIdAsync(complexId, cancellationToken);
        if (complex is null)
        {
            return Result.Failure<Guid>(ComplexErrors.NotFound);
        }

        var courtResult = Court.Create(
            CourtId.New(),
            complexId,
            request.Name,
            _clock.UtcNow);

        if (courtResult.IsFailure)
        {
            return Result.Failure<Guid>(courtResult.Error);
        }

        await _courts.AddAsync(courtResult.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(courtResult.Value.Id.Value);
    }
}
