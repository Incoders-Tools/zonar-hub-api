using MediatR;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.TournamentModalities;
using ZonarHub.Application.Features.TournamentModalities.GetAll;

namespace ZonarHub.ApiService.Endpoints.Admin.TournamentModalities;

public static class TournamentModalitiesEndpointsExtensions
{
    private const string Tag = "Admin.TournamentModalities";
    private const string RoutePrefix = "/api/admin/tournament-modalities";

    public static IEndpointRouteBuilder MapTournamentModalitiesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .RequireAuthorization();

        group.MapGet("/", GetAllAsync)
            .WithName("GetAllTournamentModalities")
            .WithSummary("Get all tournament modalities")
            .WithDescription("Returns all active tournament modalities (Individual, Doubles, Teams).")
            .Produces<TournamentModalityListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetAllAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTournamentModalitiesQuery(), cancellationToken);
        return result.Match(items => (IResult)TypedResults.Ok(new TournamentModalityListResponse(items)));
    }
}
