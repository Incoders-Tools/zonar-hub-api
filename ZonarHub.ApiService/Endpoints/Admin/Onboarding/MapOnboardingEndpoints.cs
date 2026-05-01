using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.Onboarding.CompleteOnboarding;
using ZonarHub.Domain.Organizations;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ZonarHub.ApiService.Endpoints.Admin.Onboarding;

public static class OnboardingEndpointsExtensions
{
    private const string Tag = "Admin.Onboarding";
    private const string RoutePrefix = "/api/admin/onboarding";

    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix).WithTags(Tag);

        group.MapPost("/complete", CompleteAsync)
            .WithName("CompleteOnboarding")
            .WithSummary("Persist all data collected during the first-run onboarding wizard")
            .Produces<CompleteOnboardingResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization("AdminOrAbove");

        return app;
    }

    private static async Task<IResult> CompleteAsync(
        [FromBody] CompleteOnboardingRequest body,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var tenantId = httpContext.GetTenantId() ?? body.TenantId;

        if (!Enum.TryParse<OrganizationType>(body.OrganizationType, ignoreCase: true, out var orgType))
        {
            return TypedResults.Problem(
                detail: "onboarding.errors.org_type_invalid",
                statusCode: StatusCodes.Status400BadRequest);
        }

        OnboardingTournamentInput? tournament = null;
        if (body.Tournament is { } t)
        {
            if (!DateOnly.TryParse(t.StartDate, out var startDate) ||
                !DateOnly.TryParse(t.EndDate, out var endDate))
            {
                return TypedResults.Problem(
                    detail: "onboarding.errors.tournament_date_format_invalid",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            tournament = new OnboardingTournamentInput(t.Name, startDate, endDate);
        }

        var command = new CompleteOnboardingCommand(
            tenantId,
            body.CreatedByUserId,
            body.OrganizationDisplayName,
            orgType,
            body.SystemSettings is { } ss
                ? new OnboardingSystemSettingsInput(ss.Locale, ss.Theme, ss.Timezone, ss.DateFormat)
                : null,
            body.Venue is { } v
                ? new OnboardingVenueInput(v.Name, v.Address, v.Location, v.CourtNames)
                : null,
            body.EnabledSportIds,
            tournament);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(r => (IResult)TypedResults.Created($"{RoutePrefix}/complete", r));
    }
}

// ---- Request DTOs ----

public sealed record CompleteOnboardingRequest(
    Guid TenantId,
    Guid CreatedByUserId,
    string OrganizationDisplayName,
    string OrganizationType,
    OnboardingSystemSettingsDto? SystemSettings,
    OnboardingVenueDto? Venue,
    IEnumerable<Guid> EnabledSportIds,
    OnboardingTournamentDto? Tournament);

public sealed record OnboardingSystemSettingsDto(
    string? Locale,
    string? Theme,
    string? Timezone,
    string? DateFormat);

public sealed record OnboardingVenueDto(
    string Name,
    string Address,
    string? Location,
    IEnumerable<string> CourtNames);

public sealed record OnboardingTournamentDto(
    string Name,
    string StartDate,
    string EndDate);
