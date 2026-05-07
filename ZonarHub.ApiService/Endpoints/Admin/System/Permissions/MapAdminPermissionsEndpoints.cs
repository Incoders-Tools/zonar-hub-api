using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.AdminPermissions;
using ZonarHub.Application.Features.AdminPermissions.GetCatalog;
using ZonarHub.Application.Features.AdminPermissions.GetUserPermissions;
using ZonarHub.Application.Features.AdminUsers;
using ZonarHub.Application.Features.AdminUsers.Update;

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

        app.MapGet("/api/admin/users/{id:guid}/permissions", GetUserPermissionsAsync)
            .WithTags(Tag)
            .WithName("GetAdminUserPermissions")
            .WithSummary("Get user permissions grouped by organization")
            .WithDescription("Returns the organization-scoped permission matrix for one managed user.")
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

    private static async Task<IResult> GetUserPermissionsAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetAdminUserPermissionsQuery(id), cancellationToken);
        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

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
