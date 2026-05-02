using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.Organizations;
using ZonarHub.Application.Features.Organizations.Create;
using ZonarHub.Application.Features.Organizations.Delete;
using ZonarHub.Application.Features.Organizations.GetAll;
using ZonarHub.Application.Features.Organizations.GetById;
using ZonarHub.Application.Features.Organizations.Update;
using ZonarHub.Domain.Organizations;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ZonarHub.ApiService.Endpoints.Admin.Organizations;

public static class OrganizationsEndpointsExtensions
{
    private const string Tag = "Admin.Organizations";
    private const string RoutePrefix = "/api/admin/organizations";

    public static IEndpointRouteBuilder MapOrganizationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix).WithTags(Tag);

        group.MapGet("/", ListAsync)
            .WithName("ListOrganizations")
            .WithSummary("List organizations for the current tenant")
            .Produces<PageResult<OrganizationResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetOrganizationById")
            .WithSummary("Get a single organization by id")
            .Produces<OrganizationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateOrganization")
            .WithSummary("Create a new organization")
            .Produces<OrganizationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization("AdminOrAbove");

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateOrganization")
            .WithSummary("Update an organization")
            .Produces<OrganizationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization("AdminOrAbove");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteOrganization")
            .WithSummary("Delete an organization")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization("AdminOrAbove");

        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] string? name = null,
        [FromQuery] string? type = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var tenantId = httpContext.GetTenantId();
        var filter = new OrganizationFilter(tenantId, name, type, isActive, page, pageSize);
        var result = await sender.Send(new GetOrganizationsQuery(filter), cancellationToken);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var tenantId = httpContext.GetTenantId();
        var result = await sender.Send(new GetOrganizationByIdQuery(id, tenantId), cancellationToken);
        return result.Match(org => (IResult)TypedResults.Ok(org));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateOrganizationRequest body,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var tenantId = httpContext.GetTenantId() ?? body.TenantId ?? Guid.Empty;

        if (tenantId == Guid.Empty)
        {
            return TypedResults.Problem("Tenant ID is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!Enum.TryParse<OrganizationType>(body.Type, ignoreCase: true, out var orgType))
        {
            return TypedResults.Problem("Invalid organization type.", statusCode: StatusCodes.Status400BadRequest);
        }

        var command = new CreateOrganizationCommand(
            tenantId,
            body.DisplayName,
            body.LegalName,
            body.Description,
            orgType,
            body.LogoUrl,
            body.CreatedByUserId);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(org => TypedResults.Created($"{RoutePrefix}/{org.Id}", org));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateOrganizationRequest body,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var tenantId = httpContext.GetTenantId();

        if (!Enum.TryParse<OrganizationType>(body.Type, ignoreCase: true, out var orgType))
        {
            return TypedResults.Problem("Invalid organization type.", statusCode: StatusCodes.Status400BadRequest);
        }

        var command = new UpdateOrganizationCommand(
            id,
            body.DisplayName,
            body.LegalName,
            body.Description,
            orgType,
            body.LogoUrl,
            body.IsActive,
            tenantId);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(org => (IResult)TypedResults.Ok(org));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var tenantId = httpContext.GetTenantId();
        var result = await sender.Send(new DeleteOrganizationCommand(id, tenantId), cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

public sealed record CreateOrganizationRequest(
    Guid? TenantId,
    string DisplayName,
    string? LegalName,
    string? Description,
    string Type,
    string? LogoUrl,
    Guid CreatedByUserId);

public sealed record UpdateOrganizationRequest(
    string DisplayName,
    string? LegalName,
    string? Description,
    string Type,
    string? LogoUrl,
    bool IsActive);
