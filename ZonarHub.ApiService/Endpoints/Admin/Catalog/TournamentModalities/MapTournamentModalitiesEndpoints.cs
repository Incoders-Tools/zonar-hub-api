using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.TournamentModalities;
using ZonarHub.Application.Features.TournamentModalities.Create;
using ZonarHub.Application.Features.TournamentModalities.Delete;
using ZonarHub.Application.Features.TournamentModalities.GetAll;
using ZonarHub.Application.Features.TournamentModalities.GetById;
using ZonarHub.Application.Features.TournamentModalities.Update;

namespace ZonarHub.ApiService.Endpoints.Admin.Catalog.TournamentModalities;

public static class TournamentModalitiesEndpointsExtensions
{
    private const string Tag = "Admin.TournamentModalities";
    private const string RoutePrefix = "/api/admin/tournament-modalities";

    public static IEndpointRouteBuilder MapTournamentModalitiesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix).WithTags(Tag);

        group.MapGet("/", GetAllAsync)
            .AllowAnonymous()
            .WithName("GetAllTournamentModalities")
            .WithSummary("Get all tournament modalities")
            .WithDescription("Returns tournament modalities. Pass includeInactive=true to include inactive entries (admin).")
            .Produces<TournamentModalityListResponse>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetTournamentModalityById")
            .WithSummary("Get a tournament modality by id")
            .Produces<TournamentModalityResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateTournamentModality")
            .WithSummary("Create a tournament modality")
            .Produces<TournamentModalityResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateTournamentModality")
            .WithSummary("Update a tournament modality")
            .Produces<TournamentModalityResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteTournamentModality")
            .WithSummary("Delete a tournament modality")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetAllAsync(
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] bool includeInactive = false)
    {
        var result = await sender.Send(new GetTournamentModalitiesQuery(includeInactive), cancellationToken);
        return result.Match(items => (IResult)TypedResults.Ok(new TournamentModalityListResponse(items)));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTournamentModalityByIdQuery(id), cancellationToken);
        return result.Match(item => (IResult)TypedResults.Ok(item));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateTournamentModalityRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreateTournamentModalityCommand(
            body.NameEs,
            body.NameEn,
            body.NamePt,
            body.Key,
            body.SortOrder);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(item => TypedResults.Created($"{RoutePrefix}/{item.Id}", item));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateTournamentModalityRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new UpdateTournamentModalityCommand(
            id,
            body.NameEs,
            body.NameEn,
            body.NamePt,
            body.SortOrder,
            body.IsActive);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(item => (IResult)TypedResults.Ok(item));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteTournamentModalityCommand(id), cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

public sealed record CreateTournamentModalityRequest(
    string NameEs,
    string NameEn,
    string NamePt,
    string Key,
    int SortOrder);

public sealed record UpdateTournamentModalityRequest(
    string NameEs,
    string NameEn,
    string NamePt,
    int SortOrder,
    bool IsActive);
