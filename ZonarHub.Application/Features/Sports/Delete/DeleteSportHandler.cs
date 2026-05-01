using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Sports;
using MediatR;

namespace ZonarHub.Application.Features.Sports.Delete;

public sealed class DeleteSportHandler : IRequestHandler<DeleteSportCommand, Result>
{
    private readonly ISportRepository _sports;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteSportHandler(ISportRepository sports, IUnitOfWork unitOfWork)
    {
        _sports = sports;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteSportCommand request, CancellationToken cancellationToken)
    {
        var sport = await _sports.GetByIdAsync(new SportId(request.Id), cancellationToken);
        if (sport is null)
        {
            return Result.Failure(SportErrors.NotFound);
        }

        _sports.Remove(sport);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
