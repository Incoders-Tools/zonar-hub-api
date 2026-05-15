using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.Impersonation.Health;

/// <summary>
/// Handler for <see cref="GetImpersonationHealthQuery"/>.
/// Reads the feature flag and returns its current value.
/// Satisfies: design §9.1, §4.1.
/// </summary>
public sealed class GetImpersonationHealthHandler
    : IRequestHandler<GetImpersonationHealthQuery, Result<ImpersonationHealthResponse>>
{
    private readonly IImpersonationFeatureFlags _featureFlags;

    public GetImpersonationHealthHandler(IImpersonationFeatureFlags featureFlags)
    {
        _featureFlags = featureFlags;
    }

    public Task<Result<ImpersonationHealthResponse>> Handle(
        GetImpersonationHealthQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(new ImpersonationHealthResponse(_featureFlags.IsEnabled)));
    }
}
