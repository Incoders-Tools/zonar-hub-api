using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.Sports;
using ZonarHub.Application.Features.Sports.Create;
using ZonarHub.Application.Features.Sports.Delete;
using ZonarHub.Application.Features.Sports.GetAll;
using ZonarHub.Application.Features.Sports.GetById;
using ZonarHub.Application.Features.Sports.Update;
using ZonarHub.Domain.Sports;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ZonarHub.ApiService.Endpoints.Admin.Catalog.Sports;

public static class SportsEndpointsExtensions
{
    private const string Tag = "Admin.Sports";
    private const string RoutePrefix = "/api/admin/sports";

    public static IEndpointRouteBuilder MapSportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix).WithTags(Tag);

        group.MapGet("/", ListAsync)
            .WithName("ListSports")
            .WithSummary("List all sports")
            .Produces<PageResult<SportResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetSportById")
            .WithSummary("Get a sport by id")
            .Produces<SportResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateSport")
            .WithSummary("Create a new sport")
            .Produces<SportResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
        // TODO: .RequireAuthorization("Admin")

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateSport")
            .WithSummary("Update a sport")
            .Produces<SportResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        // TODO: .RequireAuthorization("Admin")

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteSport")
            .WithSummary("Delete a sport")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
        // TODO: .RequireAuthorization("Admin")

        return app;
    }

    private static async Task<IResult> ListAsync(
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] string? name = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var filter = new SportFilter(name, isActive, page, pageSize);
        var result = await sender.Send(new GetSportsQuery(filter), cancellationToken);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetSportByIdQuery(id), cancellationToken);
        return result.Match(s => (IResult)TypedResults.Ok(s));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateSportRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<SportIconSource>(body.IconSource, ignoreCase: true, out var iconSource))
        {
            return TypedResults.Problem("Invalid icon source.", statusCode: StatusCodes.Status400BadRequest);
        }

        var command = new CreateSportCommand(
            body.Name,
            body.Key,
            body.Icon,
            iconSource,
            body.ModalityIds,
            body.SortOrder);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(s => TypedResults.Created($"{RoutePrefix}/{s.Id}", s));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateSportRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<SportIconSource>(body.IconSource, ignoreCase: true, out var iconSource))
        {
            return TypedResults.Problem("Invalid icon source.", statusCode: StatusCodes.Status400BadRequest);
        }

        var command = new UpdateSportCommand(
            id,
            body.Name,
            body.Icon,
            iconSource,
            body.ModalityIds,
            body.SortOrder,
            body.IsActive);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(s => (IResult)TypedResults.Ok(s));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteSportCommand(id), cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

public sealed record CreateSportRequest(
    string Name,
    string Key,
    string Icon,
    string IconSource,
    IEnumerable<Guid>? ModalityIds,
    int SortOrder);

public sealed record UpdateSportRequest(
    string Name,
    string Icon,
    string IconSource,
    IEnumerable<Guid>? ModalityIds,
    int SortOrder,
    bool IsActive);
