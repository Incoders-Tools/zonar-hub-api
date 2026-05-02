using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Courts;
using MediatR;

namespace ZonarHub.Application.Features.Courts.Delete;

internal sealed class DeleteCourtHandler : IRequestHandler<DeleteCourtCommand, Result>
{
    private readonly ICourtRepository _courts;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteCourtHandler(ICourtRepository courts, IUnitOfWork unitOfWork)
    {
        _courts = courts;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteCourtCommand request, CancellationToken cancellationToken)
    {
        var court = await _courts.GetByIdAsync(new CourtId(request.Id), cancellationToken);
        if (court is null)
        {
            return Result.Failure(CourtErrors.NotFound);
        }

        _courts.Remove(court);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
