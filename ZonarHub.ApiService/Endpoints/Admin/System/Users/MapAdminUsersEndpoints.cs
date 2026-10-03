using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.AdminUsers;
using ZonarHub.Application.Features.AdminUsers.Create;
using ZonarHub.Application.Features.AdminUsers.Delete;
using ZonarHub.Application.Features.AdminUsers.GetAll;
using ZonarHub.Application.Features.AdminUsers.Update;

namespace ZonarHub.ApiService.Endpoints.Admin.System.Users;

public static class AdminUsersEndpointsExtensions
{
    private const string Tag = "Admin.Users";
    private const string RoutePrefix = "/api/admin/users";

    public static IEndpointRouteBuilder MapAdminUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix).WithTags(Tag);

        group.MapGet("/", ListAsync)
            .WithName("ListAdminUsers")
            .WithSummary("List users managed by the current admin scope")
            .WithDescription(
                "Returns a paged list of users within an explicit scope, filtered before paging and counting. " +
                "scope=organization (default) requires organizationId of an active organization the caller may manage " +
                "and returns users whose primary or assigned organization is that organization; tenant administrators " +
                "also see unassigned users of their tenant so they can assign them, while system administrators see only " +
                "associated users. scope=all returns every user, is restricted to system administrators, and must not " +
                "include organizationId. The X-Organization-Id header is never used to authorize the scope.")
            .Produces<PageResult<AdminUserResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization("AdminOrAbove");

        group.MapPost("/", CreateAsync)
            .WithName("CreateAdminUser")
            .WithSummary("Create a managed user")
            .WithDescription("Creates an administrative user and persists organization assignments and per-organization tool permissions when provided.")
            .Produces<AdminUserResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization("AdminOrAbove");

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateAdminUser")
            .WithSummary("Update a managed user")
            .WithDescription("Updates profile, role, activation, organization assignments, and per-organization tool permissions for a managed user.")
            .Produces<AdminUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization("AdminOrAbove");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteAdminUser")
            .WithSummary("Delete a managed user")
            .WithDescription("Deletes a managed user and removes all organization assignment and tool permission links.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization("AdminOrAbove");

        return app;
    }

    internal static async Task<IResult> ListAsync(
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] string? search = null,
        [FromQuery] string? roleId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? scope = null,
        [FromQuery] Guid? organizationId = null)
    {
        var filter = new AdminUserFilter(search, roleId, isActive, page, pageSize);
        var result = await sender.Send(new GetAdminUsersQuery(filter, scope, organizationId), cancellationToken);
        if (result.IsFailure && result.Error == AdminUserErrors.Forbidden)
        {
            return TypedResults.Problem(
                title: "Forbidden",
                detail: result.Error.MessageKey,
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
        }

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateAdminUserRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreateAdminUserCommand(
            body.Email,
            body.FullName,
            body.Phone,
            body.RoleId,
            body.OrganizationId,
            body.TenantIds,
            body.PermissionsByOrganization?.Select(permission =>
                new AdminUserOrganizationPermissionInput(permission.OrganizationId, permission.ToolKeys)).ToList(),
            body.Password);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(user => TypedResults.Created($"{RoutePrefix}/{user.Id}", user));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateAdminUserRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new UpdateAdminUserCommand(
            id,
            body.FullName,
            body.Phone,
            body.RoleId,
            body.OrganizationId,
            body.TenantIds,
            body.PermissionsByOrganization?.Select(permission =>
                new AdminUserOrganizationPermissionInput(permission.OrganizationId, permission.ToolKeys)).ToList(),
            body.IsActive);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(user => (IResult)TypedResults.Ok(user));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteAdminUserCommand(id), cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

public sealed record CreateAdminUserRequest(
    string Email,
    string FullName,
    string? Phone,
    string RoleId,
    Guid? OrganizationId,
    IReadOnlyList<Guid>? TenantIds,
    IReadOnlyList<OrganizationPermissionAssignmentRequest>? PermissionsByOrganization,
    string? Password);

public sealed record UpdateAdminUserRequest(
    string? FullName,
    string? Phone,
    string? RoleId,
    Guid? OrganizationId,
    IReadOnlyList<Guid>? TenantIds,
    IReadOnlyList<OrganizationPermissionAssignmentRequest>? PermissionsByOrganization,
    bool? IsActive);

public sealed record OrganizationPermissionAssignmentRequest(
    Guid OrganizationId,
    IReadOnlyList<string> ToolKeys);
