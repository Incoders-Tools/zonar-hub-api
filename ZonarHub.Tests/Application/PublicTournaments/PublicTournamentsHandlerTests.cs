using System.Text.Json;
using ZonarHub.Application.Features.PublicTournaments;
using ZonarHub.Application.Features.PublicTournaments.GetAll;
using ZonarHub.Application.Features.PublicTournaments.GetBySlug;
using ZonarHub.Domain.Organizations;
using static ZonarHub.Tests.Application.PublicTournaments.PublicTournamentsTestHarness;

namespace ZonarHub.Tests.Application.PublicTournaments;

public class PublicTournamentsHandlerTests
{
    [Fact]
    public async Task List_MapsEveryPublicFieldAndResolvesCatalogNames()
    {
        var h = new PublicTournamentsTestHarness();
        h.Tournaments.Items.Add(Record(organization: Organization(Consent())));

        var result = await h.List.Handle(new GetPublicTournamentsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value);
        Assert.Equal("open-norte-abc123", item.Slug);
        Assert.Equal("Open Norte", item.Name);
        Assert.Equal("Summer open", item.Description);
        Assert.Equal("Best of three", item.Rules);
        Assert.Equal(new DateOnly(2026, 11, 20), item.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 22), item.EndDate);
        Assert.Equal(new DateOnly(2026, 11, 1), item.RegistrationStartDate);
        Assert.Equal(new DateOnly(2026, 11, 15), item.RegistrationEndDate);
        Assert.Equal(new PublicTournamentStatusResponse("registration_open", "Inscripción abierta", "Registration open", "Inscrição aberta"), item.Status);
        Assert.Equal(new PublicSportResponse("padel", "Pádel", "/uploads/sports-icons/padel.svg", "svg"), item.Sport);
        Assert.Equal(new PublicCategoryResponse("cuarta", "Cuarta categoría", "4ta"), item.Category);
        Assert.Equal(new PublicGenderResponse("mixed", "Mixto"), item.Gender);
        Assert.Equal(new PublicModalityResponse("pairs", "Parejas", "Pairs", "Duplas"), item.Modality);
        Assert.Equal(new PublicComplexResponse("Complejo Norte", "Av. Siempre Viva 742"), item.Complex);
        Assert.Equal("https://cdn.example/t.png", item.ImageUrl);
        Assert.Equal("https://cdn.example/cover.png", item.CoverImageUrl);
        Assert.Equal(32, item.MaxPairs);
        Assert.Equal(25000m, item.RegistrationFeePerPair);
        Assert.Equal(500000m, item.PrizeMoney);
        Assert.NotNull(item.Organization);
    }

    [Fact]
    public async Task List_KeepsTheStoreOrderingByStartDate()
    {
        var h = new PublicTournamentsTestHarness();
        h.Tournaments.Items.Add(Record("first-aaaaaa", new DateOnly(2026, 11, 1)));
        h.Tournaments.Items.Add(Record("second-bbbbbb", new DateOnly(2026, 12, 1)));
        h.Tournaments.Items.Add(Record("third-cccccc", new DateOnly(2027, 1, 1)));

        var result = await h.List.Handle(new GetPublicTournamentsQuery(), CancellationToken.None);

        Assert.Equal(["first-aaaaaa", "second-bbbbbb", "third-cccccc"], result.Value.Select(t => t.Slug));
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(10, 10)]
    [InlineData(100, 100)]
    [InlineData(500, 100)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public async Task List_CapsTheLimitAt100(int? requested, int expected)
    {
        var h = new PublicTournamentsTestHarness();

        await h.List.Handle(new GetPublicTournamentsQuery(requested), CancellationToken.None);

        Assert.Equal([expected], h.Tournaments.ListLimits);
    }

    [Fact]
    public async Task List_EmptyShowcase_DoesNotReadTheCatalogs()
    {
        var h = new PublicTournamentsTestHarness();

        var result = await h.List.Handle(new GetPublicTournamentsQuery(), CancellationToken.None);

        Assert.Empty(result.Value);
        Assert.Equal(0, h.Categories.Reads);
    }

    [Fact]
    public async Task Organization_WithoutConsent_IsNull()
    {
        var h = new PublicTournamentsTestHarness();
        h.Tournaments.Items.Add(Record(organization: Organization(Consent(enabled: false))));

        var result = await h.List.Handle(new GetPublicTournamentsQuery(), CancellationToken.None);

        Assert.Null(Assert.Single(result.Value).Organization);
    }

    [Fact]
    public async Task Organization_WithConsent_CarriesProfileAndOnlyNonEmptyLinks()
    {
        var h = new PublicTournamentsTestHarness();
        h.Tournaments.Items.Add(Record(organization: Organization(Consent())));

        var result = await h.List.Handle(new GetPublicTournamentsQuery(), CancellationToken.None);

        var organization = Assert.Single(result.Value).Organization!;
        Assert.Equal("Club Norte", organization.DisplayName);
        Assert.Equal("https://cdn.example/logo.png", organization.LogoUrl);
        Assert.Equal("Padel club", organization.Description);
        Assert.Equal("circuito", organization.Type);

        using var links = JsonDocument.Parse(JsonSerializer.Serialize(
            organization.Links, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(
            ["websiteUrl", "instagramUrl", "whatsappPhone", "contactEmail"],
            links.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("https://club.example", links.RootElement.GetProperty("websiteUrl").GetString());
        Assert.Equal("https://instagram.com/club", links.RootElement.GetProperty("instagramUrl").GetString());
        Assert.Equal("+5491122334455", links.RootElement.GetProperty("whatsappPhone").GetString());
        Assert.Equal("hola@club.example", links.RootElement.GetProperty("contactEmail").GetString());
    }

    [Fact]
    public async Task Organization_WithConsentButNoLinks_SerializesAnEmptyLinksObject()
    {
        var h = new PublicTournamentsTestHarness();
        var noLinks = OrganizationPublicProfile.Create(true, null, null, null, null, null, null, null);
        h.Tournaments.Items.Add(Record(organization: Organization(noLinks)));

        var result = await h.List.Handle(new GetPublicTournamentsQuery(), CancellationToken.None);

        var links = Assert.Single(result.Value).Organization!.Links;
        Assert.Equal("{}", JsonSerializer.Serialize(links, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task Organization_InactiveWithConsent_IsNull()
    {
        var h = new PublicTournamentsTestHarness();
        h.Tournaments.Items.Add(Record(organization: Organization(Consent(), isActive: false)));

        var result = await h.List.Handle(new GetPublicTournamentsQuery(), CancellationToken.None);

        Assert.Null(Assert.Single(result.Value).Organization);
    }

    [Fact]
    public async Task Status_UnknownKey_KeepsTheKeyWithoutNames_AndMissingStatusIsNull()
    {
        var h = new PublicTournamentsTestHarness();
        h.Tournaments.Items.Add(Record("custom-aaaaaa", status: "custom_state"));
        h.Tournaments.Items.Add(Record("nostatus-bbbbbb", status: null));

        var result = await h.List.Handle(new GetPublicTournamentsQuery(), CancellationToken.None);

        Assert.Equal(new PublicTournamentStatusResponse("custom_state", null, null, null), result.Value[0].Status);
        Assert.Null(result.Value[1].Status);
    }

    [Fact]
    public async Task Catalogs_UnknownOrMissingIds_AreNull()
    {
        var h = new PublicTournamentsTestHarness();
        h.Tournaments.Items.Add(Record() with { CategoryId = Guid.NewGuid(), GenderId = null, ModalityId = Guid.NewGuid(), Sport = null, Complex = null });

        var result = await h.List.Handle(new GetPublicTournamentsQuery(), CancellationToken.None);

        var item = Assert.Single(result.Value);
        Assert.Null(item.Category);
        Assert.Null(item.Gender);
        Assert.Null(item.Modality);
        Assert.Null(item.Sport);
        Assert.Null(item.Complex);
    }

    [Fact]
    public async Task BySlug_ShowcaseTournament_ReturnsIt()
    {
        var h = new PublicTournamentsTestHarness();
        h.Tournaments.Items.Add(Record(organization: Organization(Consent())));

        var result = await h.BySlug.Handle(new GetPublicTournamentBySlugQuery("open-norte-abc123"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("open-norte-abc123", result.Value.Slug);
        Assert.Equal("Cuarta categoría", result.Value.Category!.Name);
        Assert.Equal("Club Norte", result.Value.Organization!.DisplayName);
    }

    [Fact]
    public async Task BySlug_NotInTheShowcase_ReturnsNotFound()
    {
        var h = new PublicTournamentsTestHarness();

        var result = await h.BySlug.Handle(new GetPublicTournamentBySlugQuery("hidden-cup-abc123"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PublicTournamentErrors.NotFound, result.Error);
        Assert.Equal(["hidden-cup-abc123"], h.Tournaments.SlugLookups);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData("Open-Cup-abc123")]
    [InlineData("open--cup")]
    [InlineData("-open-cup")]
    [InlineData("open-cup-")]
    [InlineData("open cup")]
    [InlineData("open_cup")]
    [InlineData("../admin")]
    [InlineData("open-cup&is_public=eq.false")]
    public async Task BySlug_MalformedSlug_ReturnsNotFoundWithoutQueryingTheStore(string slug)
    {
        var h = new PublicTournamentsTestHarness();

        var result = await h.BySlug.Handle(new GetPublicTournamentBySlugQuery(slug), CancellationToken.None);

        Assert.Equal(PublicTournamentErrors.NotFound, result.Error);
        Assert.Empty(h.Tournaments.SlugLookups);
    }

    [Fact]
    public async Task BySlug_LongerThan80Characters_ReturnsNotFoundWithoutQueryingTheStore()
    {
        var h = new PublicTournamentsTestHarness();

        var result = await h.BySlug.Handle(new GetPublicTournamentBySlugQuery(new string('a', 81)), CancellationToken.None);

        Assert.Equal(PublicTournamentErrors.NotFound, result.Error);
        Assert.Empty(h.Tournaments.SlugLookups);
    }

    [Fact]
    public void Response_Json_ExposesOnlyTheDocumentedFields()
    {
        var response = new PublicTournamentResponse(
            "open-norte-abc123", "Open", "d", "r",
            new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 22), new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 15),
            new PublicTournamentStatusResponse("registration_open", "a", "b", "c"),
            new PublicSportResponse("padel", "Pádel", "x", "svg"),
            new PublicCategoryResponse("cuarta", "Cuarta", "4ta"),
            new PublicGenderResponse("mixed", "Mixto"),
            new PublicModalityResponse("pairs", "a", "b", "c"),
            new PublicComplexResponse("Complejo", "Calle 1"),
            "i", "c", 32, 1m, 2m,
            new PublicOrganizationResponse("Club", "l", "d", "circuito",
                new PublicOrganizationLinksResponse("w", "i", "f", "t", "y", "+12345678", "a@b.io")));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = json.RootElement;

        Assert.Equal(
            [
                "slug", "name", "description", "rules", "startDate", "endDate", "registrationStartDate",
                "registrationEndDate", "status", "sport", "category", "gender", "modality", "complex", "imageUrl",
                "coverImageUrl", "maxPairs", "registrationFeePerPair", "prizeMoney", "organization",
            ],
            root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["key", "nameEs", "nameEn", "namePt"], Names(root.GetProperty("status")));
        Assert.Equal(["key", "name", "icon", "iconSource"], Names(root.GetProperty("sport")));
        Assert.Equal(["key", "name", "shortName"], Names(root.GetProperty("category")));
        Assert.Equal(["key", "name"], Names(root.GetProperty("gender")));
        Assert.Equal(["key", "nameEs", "nameEn", "namePt"], Names(root.GetProperty("modality")));
        Assert.Equal(["name", "address"], Names(root.GetProperty("complex")));
        Assert.Equal(["displayName", "logoUrl", "description", "type", "links"], Names(root.GetProperty("organization")));
        Assert.Equal(
            ["websiteUrl", "instagramUrl", "facebookUrl", "tiktokUrl", "youtubeUrl", "whatsappPhone", "contactEmail"],
            Names(root.GetProperty("organization").GetProperty("links")));

        string[] forbidden =
        [
            "id", "organizationId", "tenantId", "complexId", "courtIds", "selectedCourtIds", "createdByUserId",
            "createdBy", "observations", "sumValue", "pointsToAward", "isActive", "isPublic", "publicProfileEnabled",
            "sportId", "categoryId", "genderId", "modalityId", "tournamentTypeId", "ruleSetId", "legalName",
            "createdAtUtc", "updatedAtUtc",
        ];
        var all = AllPropertyNames(root).ToList();
        foreach (var name in forbidden)
        {
            Assert.DoesNotContain(name, all);
        }
    }

    private static IEnumerable<string> Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name);

    private static IEnumerable<string> AllPropertyNames(JsonElement element) =>
        element.ValueKind != JsonValueKind.Object
            ? []
            : element.EnumerateObject().SelectMany(p => AllPropertyNames(p.Value).Prepend(p.Name));
}
