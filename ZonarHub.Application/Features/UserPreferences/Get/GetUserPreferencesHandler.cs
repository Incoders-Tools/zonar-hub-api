using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.SystemSettings;
using MediatR;

namespace ZonarHub.Application.Features.UserPreferences.Get;

public sealed class GetUserPreferencesHandler
    : IRequestHandler<GetUserPreferencesQuery, Result<UserPreferencesResponse>>
{
    private readonly ISystemSettingRepository _settings;

    public GetUserPreferencesHandler(ISystemSettingRepository settings)
    {
        _settings = settings;
    }

    public async Task<Result<UserPreferencesResponse>> Handle(
        GetUserPreferencesQuery request,
        CancellationToken cancellationToken)
    {
        var query = new SystemSettingQuery(
            Scope: SystemSettingScope.User,
            TenantId: request.OrganizationId,
            UserId: request.UserId,
            KeyContains: null,
            Page: 1,
            PageSize: 1000);

        var (items, _) = await _settings.ListAsync(query, cancellationToken);

        var preferences = items.ToDictionary(
            s => s.Key,
            s => s.Value);

        return Result.Success(new UserPreferencesResponse(preferences));
    }
}
