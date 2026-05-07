using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.TournamentStatuses;
using ZonarHub.Application.Features.TournamentStatuses.Create;
using ZonarHub.Application.Features.TournamentStatuses.Delete;
using ZonarHub.Application.Features.TournamentStatuses.GetAll;
using ZonarHub.Application.Features.TournamentStatuses.GetById;
using ZonarHub.Application.Features.TournamentStatuses.Update;

namespace ZonarHub.ApiService.Endpoints.Admin.Catalog.TournamentStatuses;

public static class TournamentStatusesEndpointsExtensions
{
    private const string Tag = "Admin.TournamentStatuses";
    private const string RoutePrefix = "/api/admin/tournament-statuses";

    public static IEndpointRouteBuilder MapTournamentStatusesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .RequireAuthorization("AdminOrAbove");

        group.MapGet("/", GetAllAsync)
            .WithName("GetAllTournamentStatuses")
            .WithSummary("Get all tournament statuses")
            .WithDescription("Returns tournament statuses with all locale variants. Pass includeInactive=false to skip disabled rows.")
            .Produces<TournamentStatusListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetTournamentStatusById")
            .WithSummary("Get tournament status by ID")
            .Produces<TournamentStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", CreateAsync)
            .WithName("CreateTournamentStatus")
            .WithSummary("Create a tournament status")
            .Produces<TournamentStatusResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateTournamentStatus")
            .WithSummary("Update a tournament status")
            .Produces<TournamentStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteTournamentStatus")
            .WithSummary("Delete a tournament status")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetAllAsync(
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] bool includeInactive = true)
    {
        var result = await sender.Send(new GetTournamentStatusesQuery(includeInactive), cancellationToken);
        return result.Match(items => (IResult)TypedResults.Ok(new TournamentStatusListResponse(items)));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTournamentStatusByIdQuery(id), cancellationToken);
        return result.Match(item => (IResult)TypedResults.Ok(item));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateTournamentStatusRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateTournamentStatusCommand(
                body.Key,
                body.NameEs,
                body.NameEn,
                body.NamePt,
                body.DescriptionEs,
                body.DescriptionEn,
                body.DescriptionPt,
                body.SortOrder),
            cancellationToken);

        return result.Match(item => TypedResults.Created($"{RoutePrefix}/{item.Id}", item));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateTournamentStatusRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpdateTournamentStatusCommand(
                id,
                body.NameEs,
                body.NameEn,
                body.NamePt,
                body.DescriptionEs,
                body.DescriptionEn,
                body.DescriptionPt,
                body.SortOrder,
                body.IsActive),
            cancellationToken);

        return result.Match(item => (IResult)TypedResults.Ok(item));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteTournamentStatusCommand(id), cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

public sealed record TournamentStatusListResponse(IReadOnlyList<TournamentStatusResponse> Items);

public sealed record CreateTournamentStatusRequest(
    string Key,
    string NameEs,
    string NameEn,
    string NamePt,
    string? DescriptionEs,
    string? DescriptionEn,
    string? DescriptionPt,
    int SortOrder);

public sealed record UpdateTournamentStatusRequest(
    string NameEs,
    string NameEn,
    string NamePt,
    string? DescriptionEs,
    string? DescriptionEn,
    string? DescriptionPt,
    int SortOrder,
    bool IsActive);
