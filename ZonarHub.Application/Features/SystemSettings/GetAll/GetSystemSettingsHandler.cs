using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.SystemSettings.GetAll;

public sealed class GetSystemSettingsHandler
    : IRequestHandler<GetSystemSettingsQuery, Result<PageResult<SystemSettingResponse>>>
{
    private readonly ISystemSettingRepository _settings;

    public GetSystemSettingsHandler(ISystemSettingRepository settings)
    {
        _settings = settings;
    }

    public async Task<Result<PageResult<SystemSettingResponse>>> Handle(
        GetSystemSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(request.Filter);
        var repoQuery = new SystemSettingQuery(
            normalized.Scope,
            normalized.TenantId,
            normalized.UserId,
            normalized.Key,
            normalized.Page,
            normalized.PageSize);

        var (items, total) = await _settings.ListAsync(repoQuery, cancellationToken);
        IReadOnlyList<SystemSettingResponse> mapped = items
            .Select(SystemSettingResponse.FromDomain)
            .ToList();

        return Result.Success(new PageResult<SystemSettingResponse>(
            mapped,
            normalized.Page,
            normalized.PageSize,
            total));
    }

    private static SystemSettingsFilter Normalize(SystemSettingsFilter filter)
    {
        var page = filter.Page < 1 ? PageRequest.DefaultPage : filter.Page;
        var pageSize = filter.PageSize switch
        {
            < 1 => PageRequest.DefaultPageSize,
            > PageRequest.MaxPageSize => PageRequest.MaxPageSize,
            _ => filter.PageSize,
        };
        var key = string.IsNullOrWhiteSpace(filter.Key) ? null : filter.Key.Trim();
        return filter with { Page = page, PageSize = pageSize, Key = key };
    }
}
