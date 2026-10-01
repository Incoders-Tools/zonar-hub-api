using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.Complexes;
using ZonarHub.Application.Features.Complexes.Create;
using ZonarHub.Application.Features.Complexes.Delete;
using ZonarHub.Application.Features.Complexes.GetAll;
using ZonarHub.Application.Features.Complexes.Update;
using ZonarHub.Application.Features.Complexes.SaveWithCourts;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.ApiService.Endpoints.Admin.Catalog.Complexes;

public static class ComplexesEndpointsExtensions
{
    private const string Tag = "Admin.Complexes";
    private const string RoutePrefix = "/api/admin/complexes";

    public static IEndpointRouteBuilder MapComplexesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .RequireAuthorization();

        group.MapPost("/save", SaveAsync)
            .RequireAuthorization("AdminOrAbove")
            .WithName("SaveComplexWithCourts")
            .WithSummary("Save a complex and courts atomically")
            .WithDescription("Creates or updates a complex and supplied courts in one transaction; omitted courts remain unchanged, explicit deleted court IDs are removed. Requires an active administrator authorized for the organization.")
            .Produces<SavedComplexWithCourtsData>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", ListAsync)
            .WithName("ListComplexes")
            .WithSummary("List complexes for an organization")
            .WithDescription("Returns all complexes belonging to the given organization.")
            .Produces<IReadOnlyList<ComplexResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/", CreateAsync)
            .WithName("CreateComplex")
            .WithSummary("Create a new complex/venue")
            .WithDescription("Creates a new complex associated to the specified organization.")
            .Produces<ComplexResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateComplex")
            .WithSummary("Update a complex/venue")
            .Produces<ComplexResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteComplex")
            .WithSummary("Delete a complex/venue")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> SaveAsync(
        SaveComplexWithCourtsCommand body, IMediator mediator, CancellationToken ct)
    {
        var result = await mediator.Send(body, ct);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.Code == "complexes.aggregate_forbidden" ? Results.Forbid() : result.Error.ToProblem();
    }

    private static async Task<IResult> ListAsync(
        [AsParameters] ListComplexesRequest req,
        IMediator mediator,
        CancellationToken ct)
    {
        if (req.OrganizationId == Guid.Empty)
            return Results.BadRequest("organizationId is required.");

        var result = await mediator.Send(new GetComplexesQuery(req.OrganizationId), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblem();
    }

    private static async Task<IResult> CreateAsync(
        CreateComplexRequest body,
        IMediator mediator,
        CancellationToken ct)
    {
        var command = new CreateComplexCommand(
            body.OrganizationId, body.Name, body.Key, body.Address, body.Location,
            body.Description, body.SortOrder, body.Preponderance,
            body.LogoImagePath, body.CoverImagePath, body.LayoutDiagramPath);
        var result = await mediator.Send(command, ct);
        return result.IsSuccess
            ? Results.Created($"{RoutePrefix}/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateComplexRequest body,
        IMediator mediator,
        CancellationToken ct)
    {
        var command = new UpdateComplexCommand(
            id, body.Name, body.Key, body.Address, body.Location,
            body.Description, body.SortOrder, body.Preponderance,
            body.LogoImagePath, body.CoverImagePath, body.LayoutDiagramPath, body.IsActive);
        var result = await mediator.Send(command, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblem();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteComplexCommand(id), ct);
        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToProblem();
    }
}

internal sealed record ListComplexesRequest([FromQuery] Guid OrganizationId);

internal sealed record CreateComplexRequest(
    Guid OrganizationId,
    string Name,
    string? Key,
    string Address,
    string? Location,
    string? Description,
    int SortOrder,
    int Preponderance,
    string? LogoImagePath,
    string? CoverImagePath,
    string? LayoutDiagramPath);

internal sealed record UpdateComplexRequest(
    string Name,
    string? Key,
    string Address,
    string? Location,
    string? Description,
    int SortOrder,
    int Preponderance,
    string? LogoImagePath,
    string? CoverImagePath,
    string? LayoutDiagramPath,
    bool IsActive);
