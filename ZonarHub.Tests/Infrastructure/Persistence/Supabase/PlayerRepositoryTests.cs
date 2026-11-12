using System.Net;
using System.Text.Json.Nodes;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Users;
using ZonarHub.Infrastructure.Persistence.Supabase;
using static ZonarHub.Tests.Infrastructure.Persistence.Supabase.SupabaseHttpRecorder;

namespace ZonarHub.Tests.Infrastructure.Persistence.Supabase;

/// <summary>PostgREST side of players (S3, D1): organization-scoped queries, snake_case rows, unique-index conflicts.</summary>
public class PlayerRepositoryTests
{
    private static readonly OrganizationId Organization = new(Guid.Parse("00000000-0000-0000-0000-0000000000b1"));
    private static readonly PlayerId Id = new(Guid.Parse("00000000-0000-0000-0000-0000000000f1"));
    private static readonly Guid User = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Gender = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid Category = Guid.Parse("b1000000-0000-0000-0000-000000000004");

    [Fact]
    public async Task GetById_IsScopedToTheOrganization_AndMapsEveryColumn()
    {
        var http = new SupabaseHttpRecorder(_ => Json($"[{RowJson()}]"));

        var player = await new PlayerRepository(http.Factory).GetByIdAsync(Organization, Id);

        var request = http.Single();
        Assert.Equal(("GET", "/rest/v1/players"), (request.Method, request.Path));
        Assert.Equal(["select=*", $"id=eq.{Id}", $"organization_id=eq.{Organization}", "limit=1"], request.Query);
        Assert.NotNull(player);
        Assert.Equal((Id, Organization, new UserId(User)), (player.Id, player.OrganizationId, player.UserId!.Value));
        Assert.Equal(("Ana", "Pérez", "ana@example.com"), (player.FirstName, player.LastName, player.Email));
        Assert.Equal(("+54 11", "30111222", "Club Norte"), (player.Phone, player.DocumentId, player.Club));
        Assert.Equal(new DateOnly(1990, 5, 1), player.BirthDate);
        Assert.Equal((Gender, Category), (player.GenderId!.Value, player.CategoryId!.Value));
        Assert.False(player.IsActive);
        Assert.Equal(new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc), player.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, player.UpdatedAtUtc.Kind);
        Assert.Equal(new DateTime(2026, 11, 2, 9, 30, 0, DateTimeKind.Utc), player.UpdatedAtUtc);
    }

    [Fact]
    public async Task GetById_NullableColumns_MapToNull_AndNoRowIsNull()
    {
        var sparse = new SupabaseHttpRecorder(_ => Json($"[{SparseRowJson()}]"));
        var none = new SupabaseHttpRecorder(_ => Json("[]"));

        var player = await new PlayerRepository(sparse.Factory).GetByIdAsync(Organization, Id);

        Assert.Null(player!.UserId);
        Assert.Null(player.Email);
        Assert.Null(player.Phone);
        Assert.Null(player.DocumentId);
        Assert.Null(player.BirthDate);
        Assert.Null(player.GenderId);
        Assert.Null(player.CategoryId);
        Assert.Null(player.Club);
        Assert.Null(await new PlayerRepository(none.Factory).GetByIdAsync(Organization, Id));
    }

    [Fact]
    public async Task GetByIds_UsesOneInFilter_AndSkipsTheCallWithoutIds()
    {
        var other = new PlayerId(Guid.Parse("00000000-0000-0000-0000-0000000000f2"));
        var http = new SupabaseHttpRecorder(_ => Json($"[{RowJson()}]"));
        var repository = new PlayerRepository(http.Factory);

        var players = await repository.GetByIdsAsync(Organization, [Id, other, Id]);

        Assert.Equal(["select=*", $"organization_id=eq.{Organization}", $"id=in.({Id},{other})"], http.Single().Query);
        Assert.Equal(Id, Assert.Single(players).Id);
        Assert.Empty(await repository.GetByIdsAsync(Organization, []));
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Search_FiltersOrdersPages_AndReadsTheExactCount()
    {
        var http = new SupabaseHttpRecorder(_ => Counted($"[{RowJson()}]", "20-20/41"));

        var page = await new PlayerRepository(http.Factory).SearchAsync(new PlayerSearch(Organization, "  Ana  ", true, 3, 10));

        var request = http.Single();
        Assert.Equal("count=exact", request.Prefer);
        Assert.Equal(
            ["select=*", $"organization_id=eq.{Organization}",
                "or=(first_name.ilike.\"*Ana*\",last_name.ilike.\"*Ana*\",email.ilike.\"*Ana*\",document_id.ilike.\"*Ana*\")",
                "is_active=eq.true", "order=last_name.asc,first_name.asc,id.asc", "offset=20", "limit=10"],
            request.Query);
        Assert.Equal((3, 10, 41, 5), (page.Page, page.PageSize, page.TotalCount, page.TotalPages));
        Assert.Equal(Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task Search_BlankTextAndNoActivity_AddNoFilter_AndPagingIsNormalized()
    {
        var http = new SupabaseHttpRecorder(_ => Counted("[]", "*/0"));

        var page = await new PlayerRepository(http.Factory).SearchAsync(new PlayerSearch(Organization, " ", null, 0, 5000));

        Assert.Equal(
            ["select=*", $"organization_id=eq.{Organization}", "order=last_name.asc,first_name.asc,id.asc", "offset=0", "limit=200"],
            http.Single().Query);
        Assert.Equal((1, 200, 0), (page.Page, page.PageSize, page.TotalCount));
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Search_TextCannotBreakTheLogicTree()
    {
        var http = new SupabaseHttpRecorder(_ => Json("[]"));

        var page = await new PlayerRepository(http.Factory).SearchAsync(new PlayerSearch(Organization, "a,b)(\"c\\*%", false, 1, 20));

        var or = Assert.Single(http.Single().Query, part => part.StartsWith("or=", StringComparison.Ordinal));
        Assert.Equal("or=(first_name.ilike.\"*abc*\",last_name.ilike.\"*abc*\",email.ilike.\"*abc*\",document_id.ilike.\"*abc*\")", or);
        Assert.Contains("is_active=eq.false", http.Single().Query);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Search_OnlySeparatorText_AddsNoFilter()
    {
        var http = new SupabaseHttpRecorder(_ => Json("[]"));

        await new PlayerRepository(http.Factory).SearchAsync(new PlayerSearch(Organization, " ,() ", null, 1, 20));

        Assert.DoesNotContain(http.Single().Query, part => part.StartsWith("or=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindByEmail_ComparesTheNormalizedEmailExactly_AndBlankMakesNoCall()
    {
        var http = new SupabaseHttpRecorder(_ => Json($"[{RowJson()}]"));
        var repository = new PlayerRepository(http.Factory);

        var player = await repository.FindByEmailAsync(Organization, "  Ana+Club_1@Example.com ");

        var request = http.Single();
        Assert.Equal(["select=*", $"organization_id=eq.{Organization}", "email=eq.ana+club_1@example.com", "limit=1"], request.Query);
        Assert.Contains("email=eq.ana%2Bclub_1%40example.com", request.RawQuery);
        Assert.Equal(Id, player!.Id);
        Assert.Null(await repository.FindByEmailAsync(Organization, "  "));
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task FindByDocument_ComparesTheTrimmedDocument_AndBlankMakesNoCall()
    {
        var http = new SupabaseHttpRecorder(_ => Json("[]"));
        var repository = new PlayerRepository(http.Factory);

        Assert.Null(await repository.FindByDocumentAsync(Organization, " 30.111&222 "));

        var request = http.Single();
        Assert.Equal(["select=*", $"organization_id=eq.{Organization}", "document_id=eq.30.111&222", "limit=1"], request.Query);
        Assert.Contains("document_id=eq.30.111%26222", request.RawQuery);
        Assert.Null(await repository.FindByDocumentAsync(Organization, ""));
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Add_PostsEverySnakeCaseColumn()
    {
        var http = new SupabaseHttpRecorder(_ => new HttpResponseMessage(HttpStatusCode.Created));

        await new PlayerRepository(http.Factory).AddAsync(SamplePlayer());

        var request = http.Single();
        Assert.Equal(("POST", "/rest/v1/players", "return=minimal"), (request.Method, request.Path, request.Prefer));
        var body = JsonNode.Parse(request.Body!)!.AsObject();
        Assert.Equal(
            ["id", "organization_id", "user_id", "first_name", "last_name", "email", "phone", "document_id", "birth_date",
                "gender_id", "category_id", "club", "is_active", "created_at_utc", "updated_at_utc"],
            body.Select(p => p.Key));
        Assert.Equal(Id.ToString(), body["id"]!.GetValue<string>());
        Assert.Equal(Organization.ToString(), body["organization_id"]!.GetValue<string>());
        Assert.Equal(User.ToString(), body["user_id"]!.GetValue<string>());
        Assert.Equal("1990-05-01", body["birth_date"]!.GetValue<string>());
        Assert.Equal("ana@example.com", body["email"]!.GetValue<string>());
        Assert.True(body["is_active"]!.GetValue<bool>());
        Assert.Equal("2026-11-01T08:00:00+00:00", body["created_at_utc"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("ux_players_organization_email", "players.email_taken")]
    [InlineData("ux_players_organization_document", "players.document_taken")]
    [InlineData("ux_players_organization_user", "players.user_taken")]
    public async Task Writes_UniqueViolations_AreConflictsWithThePlayerError(string constraint, string code)
    {
        var http = new SupabaseHttpRecorder(_ => Problem(
            HttpStatusCode.Conflict, "23505", $"duplicate key value violates unique constraint \"{constraint}\""));
        var repository = new PlayerRepository(http.Factory);

        var added = await Assert.ThrowsAsync<PersistenceConflictException>(() => repository.AddAsync(SamplePlayer()));
        var updated = await Assert.ThrowsAsync<PersistenceConflictException>(() => repository.UpdateAsync(SamplePlayer()));

        Assert.Equal(code, added.Error.Code);
        Assert.Equal(code, updated.Error.Code);
    }

    [Fact]
    public async Task Writes_OtherFailures_KeepTheHttpRequestExceptionPath()
    {
        var otherUnique = new SupabaseHttpRecorder(_ => Problem(HttpStatusCode.Conflict, "23505", "duplicate key value violates unique constraint \"players_pkey\""));
        var foreignKey = new SupabaseHttpRecorder(_ => Problem(HttpStatusCode.Conflict, "23503", "insert or update violates foreign key constraint \"ux_players_organization_email\""));
        var notJson = new SupabaseHttpRecorder(_ => new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("conflict") });
        var serverError = new SupabaseHttpRecorder(_ => Problem(HttpStatusCode.InternalServerError, "XX000", "boom"));
        var noMessage = new SupabaseHttpRecorder(_ => Json("""{"code":"23505","message":null}""", HttpStatusCode.Conflict));

        await Assert.ThrowsAsync<HttpRequestException>(() => new PlayerRepository(noMessage.Factory).AddAsync(SamplePlayer()));

        await Assert.ThrowsAsync<HttpRequestException>(() => new PlayerRepository(otherUnique.Factory).AddAsync(SamplePlayer()));
        await Assert.ThrowsAsync<HttpRequestException>(() => new PlayerRepository(foreignKey.Factory).AddAsync(SamplePlayer()));
        await Assert.ThrowsAsync<HttpRequestException>(() => new PlayerRepository(notJson.Factory).UpdateAsync(SamplePlayer()));
        await Assert.ThrowsAsync<HttpRequestException>(() => new PlayerRepository(serverError.Factory).UpdateAsync(SamplePlayer()));
    }

    [Fact]
    public async Task Update_PatchesTheOrganizationsPlayer_WithoutIdentityOrCreation()
    {
        var http = new SupabaseHttpRecorder(_ => Json($"[{RowJson()}]"));

        await new PlayerRepository(http.Factory).UpdateAsync(SamplePlayer());

        var request = http.Single();
        Assert.Equal(("PATCH", "/rest/v1/players", "return=representation"), (request.Method, request.Path, request.Prefer));
        Assert.Equal([$"id=eq.{Id}", $"organization_id=eq.{Organization}"], request.Query);
        var body = JsonNode.Parse(request.Body!)!.AsObject();
        Assert.Equal(
            ["user_id", "first_name", "last_name", "email", "phone", "document_id", "birth_date", "gender_id", "category_id",
                "club", "is_active", "updated_at_utc"],
            body.Select(p => p.Key));
    }

    [Fact]
    public async Task Update_NoMatchingRow_IsNotFound()
    {
        var http = new SupabaseHttpRecorder(_ => Json("[]"));

        var error = await Assert.ThrowsAsync<PersistenceConflictException>(() => new PlayerRepository(http.Factory).UpdateAsync(SamplePlayer()));

        Assert.Equal("players.not_found", error.Error.Code);
    }

    [Fact]
    public async Task Reads_StoreFailures_ThrowHttpRequestException()
    {
        var http = new SupabaseHttpRecorder(_ => Problem(HttpStatusCode.InternalServerError, "XX000", "boom"));
        var repository = new PlayerRepository(http.Factory);

        await Assert.ThrowsAsync<HttpRequestException>(() => repository.GetByIdAsync(Organization, Id));
        await Assert.ThrowsAsync<HttpRequestException>(() => repository.SearchAsync(new PlayerSearch(Organization, null, null, 1, 20)));
    }

    private static Player SamplePlayer() => Player.Reconstitute(
        Id,
        Organization,
        new UserId(User),
        new PlayerDetails("Ana", "Pérez", "ana@example.com", "+54 11", "30111222", new DateOnly(1990, 5, 1), Gender, Category, "Club Norte"),
        isActive: true,
        new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 11, 2, 9, 30, 0, DateTimeKind.Utc));

    internal static string RowJson() => $$"""
        {"id":"{{Id}}","organization_id":"{{Organization}}","user_id":"{{User}}","first_name":"Ana","last_name":"Pérez",
         "email":"ana@example.com","phone":"+54 11","document_id":"30111222","birth_date":"1990-05-01",
         "gender_id":"{{Gender}}","category_id":"{{Category}}","club":"Club Norte","is_active":false,
         "created_at_utc":"2026-11-01T08:00:00+00:00","updated_at_utc":"2026-11-02T06:30:00-03:00"}
        """;

    private static string SparseRowJson() => $$"""
        {"id":"{{Id}}","organization_id":"{{Organization}}","user_id":null,"first_name":"Ana","last_name":"Pérez",
         "email":null,"phone":null,"document_id":null,"birth_date":null,"gender_id":null,"category_id":null,"club":null,
         "is_active":true,"created_at_utc":"2026-11-01T08:00:00+00:00","updated_at_utc":"2026-11-01T08:00:00+00:00"}
        """;
}
