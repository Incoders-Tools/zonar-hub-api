using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Complexes;

namespace ZonarHub.Application.Features.Complexes.Delete;

public sealed record DeleteComplexCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteComplexHandler : IRequestHandler<DeleteComplexCommand, Result>
{
    private readonly IComplexRepository _complexes;

    public DeleteComplexHandler(IComplexRepository complexes)
    {
        _complexes = complexes;
    }

    public async Task<Result> Handle(DeleteComplexCommand request, CancellationToken cancellationToken)
    {
        var deleted = await _complexes.DeleteAsync(new ComplexId(request.Id), cancellationToken);
        return deleted
            ? Result.Success()
            : Result.Failure(ComplexErrors.NotFound);
    }
}
