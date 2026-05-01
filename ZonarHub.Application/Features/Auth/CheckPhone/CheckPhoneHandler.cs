using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Auth.CheckPhone;

public sealed class CheckPhoneHandler : IRequestHandler<CheckPhoneQuery, Result<bool>>
{
    private readonly IUserRepository _users;

    public CheckPhoneHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<Result<bool>> Handle(CheckPhoneQuery request, CancellationToken cancellationToken)
    {
        var normalized = request.Phone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
        var exists = await _users.ExistsByPhoneAsync(normalized, cancellationToken);
        return Result.Success(exists);
    }
}
