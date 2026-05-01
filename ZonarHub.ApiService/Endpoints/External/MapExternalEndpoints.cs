using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.External.GetExchangeRate;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace ZonarHub.ApiService.Endpoints.External;

/// <summary>
/// Minimal API routes demonstrating the external-service anti-corruption layer.
/// </summary>
public static class ExternalEndpointsExtensions
{
    private const string Tag = "External";

    public static IEndpointRouteBuilder MapExternalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/external-example")
            .WithTags(Tag);

        group.MapGet("/", GetExchangeRateAsync)
            .WithName("GetExchangeRateExample")
            .WithSummary("Fetches an exchange rate from the external provider through the ACL")
            .Produces<ExchangeRateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> GetExchangeRateAsync(
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery(Name = "base")] string baseCurrency = "USD",
        [FromQuery(Name = "quote")] string quoteCurrency = "EUR")
    {
        var result = await sender.Send(new GetExchangeRateQuery(baseCurrency, quoteCurrency), cancellationToken);
        return result.Match(rate => (IResult)TypedResults.Ok(rate));
    }
}
