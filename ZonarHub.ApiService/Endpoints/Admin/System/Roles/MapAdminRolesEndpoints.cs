using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.AdminRoles;
using ZonarHub.Application.Features.AdminRoles.Create;
using ZonarHub.Application.Features.AdminRoles.Delete;
using ZonarHub.Application.Features.AdminRoles.GetAll;
using ZonarHub.Application.Features.AdminRoles.GetById;
using ZonarHub.Application.Features.AdminRoles.Update;

namespace ZonarHub.ApiService.Endpoints.Admin.System.Roles;

public static class AdminRolesEndpointsExtensions
{
    private const string Tag = "Admin.Roles";
    private const string RoutePrefix = "/api/admin/roles";

    public static IEndpointRouteBuilder MapAdminRolesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .RequireAuthorization("SystemAdminOnly");

        group.MapGet("/", GetAllAsync)
            .WithName("GetAllAdminRoles")
            .WithSummary("Get all roles")
            .WithDescription("Returns all roles ordered by role name.")
            .Produces<AdminRoleListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id}", GetByIdAsync)
            .WithName("GetAdminRoleById")
            .WithSummary("Get role by ID")
            .WithDescription("Returns one role by its identifier.")
            .Produces<AdminRoleResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", CreateAsync)
            .WithName("CreateAdminRole")
            .WithSummary("Create a role")
            .WithDescription("Creates a custom role that can be managed by system administrators.")
            .Produces<AdminRoleResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id}", UpdateAsync)
            .WithName("UpdateAdminRole")
            .WithSummary("Update a role")
            .WithDescription("Updates role metadata for non-system roles.")
            .Produces<AdminRoleResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/{id}", DeleteAsync)
            .WithName("DeleteAdminRole")
            .WithSummary("Delete a role")
            .WithDescription("Deletes a non-system role by ID.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetAllAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetAdminRolesQuery(), cancellationToken);
        return result.Match(items => (IResult)TypedResults.Ok(new AdminRoleListResponse(items)));
    }

    private static async Task<IResult> GetByIdAsync(
        string id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetAdminRoleByIdQuery(id), cancellationToken);
        return result.Match(item => (IResult)TypedResults.Ok(item));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateAdminRoleRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateAdminRoleCommand(body.Name, body.Description, body.IsActive),
            cancellationToken);

        return result.Match(item => TypedResults.Created($"{RoutePrefix}/{item.Id}", item));
    }

    private static async Task<IResult> UpdateAsync(
        string id,
        [FromBody] UpdateAdminRoleRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpdateAdminRoleCommand(id, body.Name, body.Description, body.IsActive),
            cancellationToken);

        return result.Match(item => (IResult)TypedResults.Ok(item));
    }

    private static async Task<IResult> DeleteAsync(
        string id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteAdminRoleCommand(id), cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

public sealed record AdminRoleListResponse(IReadOnlyList<AdminRoleResponse> Items);

public sealed record CreateAdminRoleRequest(
    string Name,
    string Description,
    bool IsActive);

public sealed record UpdateAdminRoleRequest(
    string? Name,
    string? Description,
    bool? IsActive);
