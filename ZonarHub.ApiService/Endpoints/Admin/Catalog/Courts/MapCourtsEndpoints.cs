using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.Courts.Create;
using ZonarHub.Application.Features.Courts.Delete;
using ZonarHub.Application.Features.Courts.GetAll;
using ZonarHub.Application.Features.Courts.GetById;
using ZonarHub.Application.Features.Courts.GetByComplexId;
using ZonarHub.Application.Features.Courts.Update;

namespace ZonarHub.ApiService.Endpoints.Admin.Catalog.Courts;

public static class CourtsEndpointsExtensions
{
    private const string Tag = "Admin.Courts";
    private const string RoutePrefix = "/api/admin/courts";

    public static IEndpointRouteBuilder MapCourtsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .RequireAuthorization("AdminOrAbove");

        group.MapGet("/", GetAllAsync)
            .WithName("GetAllCourts")
            .WithSummary("Get all courts")
            .WithDescription("Returns all courts across all complexes.")
            .Produces<IReadOnlyList<CourtDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetCourtById")
            .WithSummary("Get court by ID")
            .Produces<CourtDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/complex/{complexId:guid}", GetByComplexIdAsync)
            .WithName("GetCourtsByComplexId")
            .WithSummary("Get courts by complex ID")
            .WithDescription("Returns all courts for a specific complex.")
            .Produces<IReadOnlyList<CourtDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", CreateAsync)
            .WithName("CreateCourt")
            .WithSummary("Create a new court")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateCourt")
            .WithSummary("Update a court")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteCourt")
            .WithSummary("Delete a court")
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
        var courts = await sender.Send(new GetAllCourtsQuery(), cancellationToken);
        return TypedResults.Ok(courts);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCourtByIdQuery(id), cancellationToken);
        return result.Match(court => (IResult)TypedResults.Ok(court));
    }

    private static async Task<IResult> GetByComplexIdAsync(
        Guid complexId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var courts = await sender.Send(new GetCourtsByComplexIdQuery(complexId), cancellationToken);
        return TypedResults.Ok(courts);
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateCourtRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreateCourtCommand(body.ComplexId, body.Name);
        var result = await sender.Send(command, cancellationToken);
        return result.Match(id => TypedResults.Created($"{RoutePrefix}/{id}", id));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateCourtRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new UpdateCourtCommand(id, body.Name, body.IsActive);
        var result = await sender.Send(command, cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteCourtCommand(id), cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

public sealed record CreateCourtRequest(Guid ComplexId, string Name);

public sealed record UpdateCourtRequest(string Name, bool IsActive);
