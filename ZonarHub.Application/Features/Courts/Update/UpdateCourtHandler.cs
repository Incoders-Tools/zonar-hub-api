using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Courts;
using MediatR;

namespace ZonarHub.Application.Features.Courts.Update;

internal sealed class UpdateCourtHandler : IRequestHandler<UpdateCourtCommand, Result>
{
    private readonly ICourtRepository _courts;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateCourtHandler(ICourtRepository courts, IUnitOfWork unitOfWork)
    {
        _courts = courts;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateCourtCommand request, CancellationToken cancellationToken)
    {
        var court = await _courts.GetByIdAsync(new CourtId(request.Id), cancellationToken);
        if (court is null)
        {
            return Result.Failure(CourtErrors.NotFound);
        }

        var updateResult = court.Update(request.Name, request.IsActive);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        _courts.Update(court);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
