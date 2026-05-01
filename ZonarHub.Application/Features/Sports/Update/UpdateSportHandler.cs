using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Sports;
using MediatR;

namespace ZonarHub.Application.Features.Sports.Update;

public sealed class UpdateSportHandler : IRequestHandler<UpdateSportCommand, Result<SportResponse>>
{
    private readonly ISportRepository _sports;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpdateSportHandler(ISportRepository sports, IUnitOfWork unitOfWork, IClock clock)
    {
        _sports = sports;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SportResponse>> Handle(
        UpdateSportCommand request,
        CancellationToken cancellationToken)
    {
        var sport = await _sports.GetByIdAsync(new SportId(request.Id), cancellationToken);
        if (sport is null)
        {
            return Result.Failure<SportResponse>(SportErrors.NotFound);
        }

        var updated = sport.Update(
            request.Name,
            request.Icon,
            request.IconSource,
            request.ModalityIds,
            request.SortOrder,
            request.IsActive,
            _clock.UtcNow);

        if (updated.IsFailure)
        {
            return Result.Failure<SportResponse>(updated.Error);
        }

        _sports.Update(sport);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SportResponse.FromDomain(sport));
    }
}
