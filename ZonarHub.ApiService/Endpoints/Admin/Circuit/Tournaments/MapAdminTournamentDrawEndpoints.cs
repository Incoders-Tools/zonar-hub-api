using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Application.Features.TournamentDraws.Delete;
using ZonarHub.Application.Features.TournamentDraws.Get;
using ZonarHub.Application.Features.TournamentDraws.Preview;
using ZonarHub.Application.Features.TournamentDraws.Results;
using ZonarHub.Application.Features.TournamentDraws.Save;
using ZonarHub.Application.Features.TournamentDraws.Status;
using ZonarHub.Domain.Common;

namespace ZonarHub.ApiService.Endpoints.Admin.Circuit.Tournaments;

/// <summary>
/// Admin draw of a tournament (D5): one draw per tournament, draft then published. Every endpoint applies the
/// tournament tenant rule: unknown tournaments return 404 (<c>tournaments.not_found</c>), tournaments outside the
/// caller's tenant 403 (<c>tournaments.forbidden</c>) before anything else.
/// </summary>
public static class AdminTournamentDrawEndpointsExtensions
{
    private const string Tag = "Admin.TournamentDraws";
    private const string RoutePrefix = "/api/admin/tournaments/{tournamentId:guid}/draw";

    private const string RequestDescription =
        "Enum values are camelCase (format singleElimination|roundRobin|groupsThenKnockout, participationFormat individual|pair|team, randomization.mode random|seeded). randomization.seed is an unsigned 64-bit decimal string; when absent or blank the API generates one and returns it, and the same request with that seed reproduces the draw. Invalid requests return 400 with a draws.* code.";

    public static IEndpointRouteBuilder MapAdminTournamentDrawEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .RequireAuthorization("AdminOrAbove");

        group.MapPost("/preview", PreviewAsync)
            .WithName("PreviewAdminTournamentDraw")
            .WithSummary("Generate a draw without saving it")
            .WithDescription($"Returns the generated draw (status draft) with its seed and empty standings; nothing is stored. {RequestDescription}")
            .Produces<TournamentDrawResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/", SaveAsync)
            .WithName("SaveAdminTournamentDraw")
            .WithSummary("Save or replace the draft draw")
            .WithDescription($"Generates the draw and stores it as a draft (a published draw returns to draft). 409 draws.results_exist when a match has a result other than scheduled. {RequestDescription}")
            .Produces<TournamentDrawResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", GetAsync)
            .WithName("GetAdminTournamentDraw")
            .WithSummary("Get the tournament draw")
            .WithDescription("Returns the stored draw with its results, resolved slots (resolvedParticipantId) and standings (a round robin's under groups[0] with id RR). Includes separation keys and conflicts. 404 draws.not_found when the tournament has no draw.")
            .Produces<TournamentDrawResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/publish", PublishAsync)
            .WithName("PublishAdminTournamentDraw")
            .WithSummary("Publish the draw")
            .WithDescription("Makes the draw visible publicly when the tournament is public and returns it. Publishing a published draw keeps its publication time. 404 draws.not_found when the tournament has no draw.")
            .Produces<TournamentDrawResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/unpublish", UnpublishAsync)
            .WithName("UnpublishAdminTournamentDraw")
            .WithSummary("Return the draw to draft")
            .WithDescription("Hides the draw from the public grid without losing results and returns it. 404 draws.not_found when the tournament has no draw.")
            .Produces<TournamentDrawResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/", DeleteAsync)
            .WithName("DeleteAdminTournamentDraw")
            .WithSummary("Delete the draft draw")
            .WithDescription("Deletes a draft whose matches have no result other than scheduled. 404 draws.not_found when there is no draw, 409 draws.results_exist when results exist, 409 draws.not_draft when the draw is published.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/matches/{matchId}/result", RecordResultAsync)
            .WithName("RecordAdminTournamentDrawMatchResult")
            .WithSummary("Record, replace or clear a match result")
            .WithDescription("Status scheduled|inProgress|completed|walkover|retired, sets [{ home, away, tiebreak: { home, away } | null }], winnerSide home|away, optional courtId, courtName and ISO-8601 times (stored in UTC). Status scheduled without sets clears the score. Returns the draw re-resolved. 400 invalid score (draws.results.*), 404 draws.results.unknown_match, 409 draws.results.bye_match, participants_pending or locked_by_next_match.")
            .Produces<TournamentDrawResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    internal static async Task<IResult> PreviewAsync(
        Guid tournamentId,
        [FromBody] TournamentDrawRequestBody body,
        ISender sender,
        CancellationToken cancellationToken) =>
        Draw(await sender.Send(new PreviewTournamentDrawCommand(tournamentId, body), cancellationToken));

    internal static async Task<IResult> SaveAsync(
        Guid tournamentId,
        [FromBody] TournamentDrawRequestBody body,
        ISender sender,
        CancellationToken cancellationToken) =>
        Draw(await sender.Send(new SaveTournamentDrawCommand(tournamentId, body), cancellationToken));

    internal static async Task<IResult> GetAsync(
        Guid tournamentId,
        ISender sender,
        CancellationToken cancellationToken) =>
        Draw(await sender.Send(new GetTournamentDrawQuery(tournamentId), cancellationToken));

    internal static async Task<IResult> PublishAsync(
        Guid tournamentId,
        ISender sender,
        CancellationToken cancellationToken) =>
        Draw(await sender.Send(new SetTournamentDrawStatusCommand(tournamentId, TournamentDrawStatus.Published), cancellationToken));

    internal static async Task<IResult> UnpublishAsync(
        Guid tournamentId,
        ISender sender,
        CancellationToken cancellationToken) =>
        Draw(await sender.Send(new SetTournamentDrawStatusCommand(tournamentId, TournamentDrawStatus.Draft), cancellationToken));

    internal static async Task<IResult> DeleteAsync(
        Guid tournamentId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteTournamentDrawCommand(tournamentId), cancellationToken);
        return AdminTournamentsEndpointsExtensions.IsForbidden(result.Error)
            ? AdminTournamentsEndpointsExtensions.Forbidden(result.Error)
            : result.Match(() => (IResult)TypedResults.NoContent());
    }

    internal static async Task<IResult> RecordResultAsync(
        Guid tournamentId,
        string matchId,
        [FromBody] MatchResultBody body,
        ISender sender,
        CancellationToken cancellationToken) =>
        Draw(await sender.Send(new RecordMatchResultCommand(tournamentId, matchId, body), cancellationToken));

    private static IResult Draw(Result<TournamentDrawResponse> result) =>
        AdminTournamentsEndpointsExtensions.IsForbidden(result.Error)
            ? AdminTournamentsEndpointsExtensions.Forbidden(result.Error)
            : result.Match(draw => (IResult)TypedResults.Ok(draw));
}
