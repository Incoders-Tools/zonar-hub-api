using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Auth;
using ZonarHub.Application.Features.Auth.SendVerificationCode;
using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Auth.CheckCode;

public sealed class CheckVerificationCodeHandler
    : IRequestHandler<CheckVerificationCodeQuery, Result<bool>>
{
    private readonly ICacheStore _cache;

    public CheckVerificationCodeHandler(ICacheStore cache)
    {
        _cache = cache;
    }

    public async Task<Result<bool>> Handle(
        CheckVerificationCodeQuery request,
        CancellationToken cancellationToken)
    {
        if (AuthVerificationCodes.IsMasterBypass(request.Code))
            return Result.Success(true);

        var emailLower = request.Email.Trim().ToLowerInvariant();
        var stored = await _cache.GetAsync<string>(
            SendVerificationCodeHandler.CacheKey(emailLower), cancellationToken);

        var valid = stored is not null &&
                    string.Equals(stored, request.Code.Trim(), StringComparison.Ordinal);

        return Result.Success(valid);
    }
}
