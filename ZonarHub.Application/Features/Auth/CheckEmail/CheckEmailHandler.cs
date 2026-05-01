using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Auth.CheckEmail;

public sealed class CheckEmailHandler : IRequestHandler<CheckEmailQuery, Result<bool>>
{
    private readonly IUserRepository _users;

    public CheckEmailHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<Result<bool>> Handle(CheckEmailQuery request, CancellationToken cancellationToken)
    {
        var exists = await _users.ExistsByEmailAsync(
            request.Email.Trim().ToLowerInvariant(), cancellationToken);

        return Result.Success(exists);
    }
}
