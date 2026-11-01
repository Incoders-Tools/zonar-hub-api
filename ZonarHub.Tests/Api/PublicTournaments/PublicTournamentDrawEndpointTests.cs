using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ZonarHub.ApiService.Endpoints.Public;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.PublicTournaments;
using ZonarHub.Application.Features.PublicTournaments.GetDraw;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Domain.Draws.Results;
using ZonarHub.Domain.Common;
using ZonarHub.Tests.Application.PublicTournaments;
using static ZonarHub.Tests.Application.PublicTournaments.PublicTournamentsTestHarness;
using static ZonarHub.Tests.Application.TournamentDraws.TournamentDrawBodies;

namespace ZonarHub.Tests.Api.PublicTournaments;

/// <summary>
/// Public draw of a showcase tournament (D6): same public filters as the showcase detail, only published draws,
/// resolved with the results and serialized without admin-only data.
/// </summary>
public class PublicTournamentDrawEndpointTests
{
    private const string Slug = "open-norte-abc123";
    private const string CacheControl = "public, max-age=15";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetDraw_PublishedDraw_ReturnsTheResolvedPublicDrawWithAShortPublicCache()
    {
        var h = new PublicTournamentsTestHarness();
        var tournamentId = Guid.NewGuid();
        h.Tournaments.Items.Add(Record(Slug, id: tournamentId));
        var stored = h.Draws.SeedDraft(tournamentId, Guid.NewGuid(), Knockout(4));
        h.Draws.SeedStatus(tournamentId, TournamentDrawStatus.Published);
        h.Draws.SeedResult(tournamentId, HomeWin("KO-R1-M1"));
        var context = new DefaultHttpContext();

        var result = await PublicTournamentsEndpointsExtensions.GetDrawAsync(Slug, context, new Sender(h), CancellationToken.None);

        var draw = Assert.IsType<Ok<TournamentDrawResponse>>(result).Value!;
        Assert.Equal(CacheControl, context.Response.Headers.CacheControl.ToString());
        Assert.Equal([tournamentId], h.Draws.PublishedLookups);
        Assert.Equal("published", draw.Status);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), draw.PublishedAt);
        Assert.Equal(stored.Structure.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture), draw.Seed);
        Assert.Equal(new KnockoutOptionsBody(false), draw.Options.Knockout);
        Assert.Equal(new ScoringOptionsResponse(3, true), draw.Options.Scoring);

        var first = Match(draw, "KO-R1-M1");
        Assert.Equal("completed", first.Result!.Status);
        Assert.Equal("home", first.Result.WinnerSide);
        var final = Match(draw, "KO-R2-M1");
        Assert.Equal("winnerOf", final.Home.Kind);
        Assert.Equal(first.Home.ParticipantId, final.Home.ResolvedParticipantId);
        Assert.Null(final.Away.ResolvedParticipantId);
        Assert.All(draw.Participants, p => Assert.Equal(["Ana", "Bea"], p.Members.Select(m => m.Split(' ')[0])));
    }

    [Fact]
    public async Task GetDraw_RoundRobin_CarriesTheStandingsOfTheResolvedResults()
    {
        var h = new PublicTournamentsTestHarness();
        var tournamentId = Guid.NewGuid();
        h.Tournaments.Items.Add(Record(Slug, id: tournamentId));
        var stored = h.Draws.SeedDraft(tournamentId, Guid.NewGuid(), RoundRobin(4));
        h.Draws.SeedStatus(tournamentId, TournamentDrawStatus.Published);
        var match = stored.Structure.Phases.Single().Matches.First(m => !m.IsBye);
        h.Draws.SeedResult(tournamentId, HomeWin(match.Id));

        var draw = Draw(await PublicTournamentsEndpointsExtensions.GetDrawAsync(
            Slug, new DefaultHttpContext(), new Sender(h), CancellationToken.None));

        var group = Assert.Single(Assert.Single(draw.Phases).Groups);
        Assert.Equal("RR", group.Id);
        Assert.Equal(4, group.Standings.Count);
        var winner = Match(draw, match.Id).Home.ParticipantId!;
        var row = group.Standings.Single(s => s.ParticipantId == winner);
        Assert.Equal((1, 1, 0, 2, 0, 12, 8, 1), (row.Played, row.Won, row.Lost, row.SetsFor, row.SetsAgainst, row.GamesFor, row.GamesAgainst, row.Position));
    }

    [Fact]
    public async Task GetDraw_StoredResultOfAMatchTheStructureNoLongerHas_IsIgnored()
    {
        var h = new PublicTournamentsTestHarness();
        var tournamentId = Guid.NewGuid();
        h.Tournaments.Items.Add(Record(Slug, id: tournamentId));
        h.Draws.SeedDraft(tournamentId, Guid.NewGuid(), Knockout(4));
        h.Draws.SeedStatus(tournamentId, TournamentDrawStatus.Published);
        h.Draws.SeedResult(tournamentId, HomeWin("KO-R3-M9"));

        var draw = Draw(await PublicTournamentsEndpointsExtensions.GetDrawAsync(
            Slug, new DefaultHttpContext(), new Sender(h), CancellationToken.None));

        Assert.All(draw.Phases.SelectMany(p => p.Rounds).SelectMany(r => r.Matches), m => Assert.Null(m.Result));
    }

    [Theory]
    [InlineData("NOT A SLUG")]
    [InlineData("ab")]
    [InlineData("open--norte")]
    public async Task GetDraw_MalformedSlug_IsTournamentNotFoundWithoutReadingTheStore(string slug)
    {
        var h = new PublicTournamentsTestHarness();
        var context = new DefaultHttpContext();

        var result = await PublicTournamentsEndpointsExtensions.GetDrawAsync(slug, context, new Sender(h), CancellationToken.None);

        AssertNotFound(result, context, PublicTournamentErrors.NotFound);
        Assert.Empty(h.Tournaments.SlugLookups);
        Assert.Empty(h.Draws.PublishedLookups);
    }

    [Fact]
    public async Task GetDraw_TournamentNotInTheShowcase_IsTournamentNotFoundWithoutReadingDraws()
    {
        var h = new PublicTournamentsTestHarness();
        var context = new DefaultHttpContext();

        var result = await PublicTournamentsEndpointsExtensions.GetDrawAsync(
            "hidden-cup-abc123", context, new Sender(h), CancellationToken.None);

        AssertNotFound(result, context, PublicTournamentErrors.NotFound);
        Assert.Equal(["hidden-cup-abc123"], h.Tournaments.SlugLookups);
        Assert.Empty(h.Draws.PublishedLookups);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetDraw_NoPublishedDraw_IsDrawNotFound(bool withDraft)
    {
        var h = new PublicTournamentsTestHarness();
        var tournamentId = Guid.NewGuid();
        h.Tournaments.Items.Add(Record(Slug, id: tournamentId));
        if (withDraft)
            h.Draws.SeedDraft(tournamentId, Guid.NewGuid(), Knockout(4));
        var context = new DefaultHttpContext();

        var result = await PublicTournamentsEndpointsExtensions.GetDrawAsync(Slug, context, new Sender(h), CancellationToken.None);

        AssertNotFound(result, context, PublicTournamentErrors.DrawNotFound);
        Assert.Equal("public_draws.not_found", PublicTournamentErrors.DrawNotFound.Code);
        Assert.Equal("public_draws.errors.not_found", PublicTournamentErrors.DrawNotFound.MessageKey);
        Assert.Equal([tournamentId], h.Draws.PublishedLookups);
    }

    [Fact]
    public async Task GetDraw_Json_HasNoAdminOnlyOrInternalData()
    {
        var h = new PublicTournamentsTestHarness();
        var tournamentId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        h.Tournaments.Items.Add(Record(Slug, id: tournamentId));
        h.Draws.SeedDraft(tournamentId, organizationId, Knockout(4, separationKey: "Club Norte"));
        h.Draws.SeedStatus(tournamentId, TournamentDrawStatus.Published);
        var courtId = Guid.NewGuid();
        await h.Draws.UpsertResultAsync(h.Draws.Find(tournamentId)!.Id, HomeWin("KO-R1-M1") with { CourtName = "Central" }, courtId);

        var draw = Draw(await PublicTournamentsEndpointsExtensions.GetDrawAsync(
            Slug, new DefaultHttpContext(), new Sender(h), CancellationToken.None));
        var json = JsonSerializer.Serialize(draw, Web);

        Assert.Equal("Central", Match(draw, "KO-R1-M1").Result!.CourtName);
        var names = AllPropertyNames(JsonDocument.Parse(json).RootElement).ToHashSet();
        foreach (var forbidden in new[] { "separationKey", "separationConflicts", "courtId", "organizationId", "tenantId", "request", "tournamentId" })
        {
            Assert.DoesNotContain(forbidden, names);
        }

        foreach (var value in new[] { tournamentId, organizationId, courtId })
        {
            Assert.DoesNotContain(value.ToString(), json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("Club Norte", json);
    }

    private static MatchResult HomeWin(string matchId) =>
        new(matchId, MatchStatus.Completed, [new SetScore(6, 4), new SetScore(6, 4)], MatchSide.Home);

    private static TournamentDrawResponse Draw(IResult result) =>
        Assert.IsType<Ok<TournamentDrawResponse>>(result).Value!;

    private static DrawMatchResponse Match(TournamentDrawResponse draw, string matchId) =>
        draw.Phases.SelectMany(p => p.Rounds).SelectMany(r => r.Matches).Single(m => m.Id == matchId);

    private static void AssertNotFound(IResult result, HttpContext context, Error error)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal(error.Code, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(error.MessageKey, problem.ProblemDetails.Detail);
        Assert.True(string.IsNullOrEmpty(context.Response.Headers.CacheControl.ToString()));
    }

    private static IEnumerable<string> AllPropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => AllPropertyNames(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(AllPropertyNames),
        _ => [],
    };

    /// <summary>Routes the public draw query to the real handler.</summary>
    private sealed class Sender(PublicTournamentsTestHarness h) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            request is GetPublicTournamentDrawQuery query
                ? (TResponse)(object)await h.Draw.Handle(query, cancellationToken)
                : throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
