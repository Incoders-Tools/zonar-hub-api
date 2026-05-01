using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Auth.ForgotPassword;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Users;
using MediatR;

namespace ZonarHub.Application.Features.Auth.ResetPassword;

public sealed class ResetPasswordHandler : IRequestHandler<ResetPasswordCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly ICacheStore _cache;
    private readonly IPasswordHasher _hasher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ResetPasswordHandler(
        IUserRepository users,
        ICacheStore cache,
        IPasswordHasher hasher,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _users = users;
        _cache = cache;
        _hasher = hasher;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var cacheKey = ForgotPasswordHandler.ResetCacheKey(request.Token);
        var email = await _cache.GetAsync<string>(cacheKey, cancellationToken);
        if (email is null)
            return Result.Failure(UserErrors.InvalidOrExpiredResetToken);

        var user = await _users.GetByEmailAsync(email, cancellationToken);
        if (user is null)
            return Result.Failure(UserErrors.NotFound);

        var newHash = _hasher.Hash(request.NewPassword);

        // Bypass the domain token check since we validate via cache
        user.SetPasswordResetToken(request.Token, _clock.UtcNow.AddMinutes(1));
        var result = user.ResetPassword(request.Token, newHash, _clock.UtcNow);
        if (result.IsFailure)
            return result;

        _users.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(cacheKey, cancellationToken);

        return Result.Success();
    }
}
