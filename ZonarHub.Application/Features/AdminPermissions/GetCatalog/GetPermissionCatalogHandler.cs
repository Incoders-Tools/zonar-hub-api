using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminPermissions.GetCatalog;

public sealed class GetPermissionCatalogHandler : IRequestHandler<GetPermissionCatalogQuery, Result<PermissionCatalogResponse>>
{
    private readonly ISystemPermissionCatalogRepository _catalog;

    public GetPermissionCatalogHandler(ISystemPermissionCatalogRepository catalog)
    {
        _catalog = catalog;
    }

    public async Task<Result<PermissionCatalogResponse>> Handle(
        GetPermissionCatalogQuery request,
        CancellationToken cancellationToken)
    {
        var modules = await _catalog.ListModulesAsync(cancellationToken);

        var response = new PermissionCatalogResponse(
            modules
                .OrderBy(module => module.SortOrder)
                .ThenBy(module => module.Key, StringComparer.OrdinalIgnoreCase)
                .Select(module => new PermissionModuleResponse(
                    module.Key,
                    module.LabelKey,
                    module.SortOrder,
                    module.IsActive,
                    module.Tools
                        .OrderBy(tool => tool.SortOrder)
                        .ThenBy(tool => tool.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(tool => new PermissionToolResponse(
                            tool.Key,
                            tool.LabelKey,
                            tool.Route,
                            tool.SortOrder,
                            tool.IsSystemAdminOnly,
                            tool.IsActive))
                        .ToList()))
                .ToList());

        return Result.Success(response);
    }
}
