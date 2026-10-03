using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.AdminPermissions;
using ZonarHub.Application.Features.AdminPermissions.GetCatalog;
using ZonarHub.Application.Features.AdminPermissions.GetUserPermissions;
using ZonarHub.Application.Features.AdminPermissions.ListPermissionSources;
using ZonarHub.Application.Features.AdminUsers;
using ZonarHub.Application.Features.AdminUsers.Update;
using ZonarHub.Domain.Common;

namespace ZonarHub.ApiService.Endpoints.Admin.System.Permissions;

public static class AdminPermissionsEndpointsExtensions
{
    private const string Tag = "Admin.Permissions";

    public static IEndpointRouteBuilder MapAdminPermissionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/permissions")
            .WithTags(Tag)
            .RequireAuthorization("AdminOrAbove");

        group.MapGet("/catalog", GetCatalogAsync)
            .WithName("GetPermissionCatalog")
            .WithSummary("Get permission catalog modules and tools")
            .WithDescription("Returns active modules and tools that can be assigned as organization-scoped permissions.")
            .Produces<PermissionCatalogResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/api/admin/users/permission-sources", ListPermissionSourcesAsync)
            .WithTags(Tag)
            .WithName("ListPermissionSourceUsers")
            .WithSummary("Search users whose permissions can be copied")
            .WithDescription(
                "Returns a paged, minimal list of users (id, fullName, email, roleId, isActive) matching search, " +
                "independent of the Users page organization or paging. System administrators search every tenant; " +
                "tenant administrators search only their own tenant and never see system administrators, filtered " +
                "before paging and counting. Inactive users are included. search is required: PostgREST " +
                "wildcards and expression delimiters are removed; at least 2 non-underscore, non-whitespace " +
                "characters must remain, otherwise 400 is returned. " +
                "page is normalized to at least 1 and pageSize is capped at 20. Organization metadata is not returned.")
            .Produces<PageResult<PermissionSourceUserResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization("AdminOrAbove");

        app.MapGet("/api/admin/users/{id:guid}/permissions", GetUserPermissionsAsync)
            .WithTags(Tag)
            .WithName("GetAdminUserPermissions")
            .WithSummary("Get user permissions grouped by organization")
            .WithDescription(
                "Returns the organization-scoped permission matrix for one managed user. System administrators see " +
                "every assigned and primary organization. Tenant administrators see only organizations that belong " +
                "to their own tenant; foreign-tenant or unresolvable organizations are omitted. " +
                "Returns 403 when the caller cannot manage the target user and 404 when the user does not exist.")
            .Produces<AdminUserPermissionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization("AdminOrAbove");

        app.MapPut("/api/admin/users/{id:guid}/permissions", UpdateUserPermissionsAsync)
            .WithTags(Tag)
            .WithName("UpdateAdminUserPermissions")
            .WithSummary("Update organization-scoped permissions for a managed user")
            .WithDescription("Replaces the user permission matrix by organization while preserving user profile data.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization("AdminOrAbove");

        return app;
    }

    private static async Task<IResult> GetCatalogAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPermissionCatalogQuery(), cancellationToken);
        return result.Match(catalog => (IResult)TypedResults.Ok(catalog));
    }

    internal static async Task<IResult> ListPermissionSourcesAsync(
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = ListPermissionSourcesLimits.MaxPageSize)
    {
        var result = await sender.Send(new ListPermissionSourcesQuery(search, page, pageSize), cancellationToken);
        if (result.IsFailure && result.Error == AdminPermissionErrors.Forbidden)
        {
            return ForbiddenProblem(result.Error);
        }

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    internal static async Task<IResult> GetUserPermissionsAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetAdminUserPermissionsQuery(id), cancellationToken);
        if (result.IsFailure && result.Error == AdminPermissionErrors.Forbidden)
        {
            return ForbiddenProblem(result.Error);
        }

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    private static IResult ForbiddenProblem(Error error) => TypedResults.Problem(
        title: "Forbidden",
        detail: error.MessageKey,
        statusCode: StatusCodes.Status403Forbidden,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    private static async Task<IResult> UpdateUserPermissionsAsync(
        Guid id,
        [FromBody] UpdateAdminUserPermissionsRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new UpdateAdminUserCommand(
            id,
            FullName: null,
            Phone: null,
            RoleId: null,
            OrganizationId: null,
            TenantIds: null,
            PermissionsByOrganization: body.PermissionsByOrganization
                .Select(permission => new AdminUserOrganizationPermissionInput(permission.OrganizationId, permission.ToolKeys))
                .ToList(),
            IsActive: null);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(_ => (IResult)TypedResults.NoContent());
    }
}

public sealed record UpdateAdminUserPermissionsRequest(
    IReadOnlyList<UserOrganizationPermissionAssignmentRequest> PermissionsByOrganization);

public sealed record UserOrganizationPermissionAssignmentRequest(
    Guid OrganizationId,
    IReadOnlyList<string> ToolKeys);
