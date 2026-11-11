using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Users;

namespace ZonarHub.Tests.Domain.Players;

/// <summary>Organization-scoped player record (S3, D1).</summary>
public class PlayerTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly OrganizationId Organization = new(Guid.Parse("00000000-0000-0000-0000-0000000000b1"));
    private static readonly Guid Gender = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid Category = Guid.Parse("b1000000-0000-0000-0000-000000000004");

    [Fact]
    public void Create_TrimsTextLowercasesEmail_AndStartsActive()
    {
        var id = PlayerId.New();
        var user = UserId.New();

        var player = Player.Create(id, Organization, user, Details(
            firstName: "  Ana ", lastName: " Pérez  ", email: "  Ana.Perez@Example.COM ", phone: " +54 11 5555 ",
            documentId: " 30111222 ", club: "  Club Norte "), Now).Value;

        Assert.Equal(id, player.Id);
        Assert.Equal(Organization, player.OrganizationId);
        Assert.Equal(user, player.UserId);
        Assert.Equal("Ana", player.FirstName);
        Assert.Equal("Pérez", player.LastName);
        Assert.Equal("Ana Pérez", player.DisplayName);
        Assert.Equal("ana.perez@example.com", player.Email);
        Assert.Equal("+54 11 5555", player.Phone);
        Assert.Equal("30111222", player.DocumentId);
        Assert.Equal(new DateOnly(1990, 5, 1), player.BirthDate);
        Assert.Equal(Gender, player.GenderId);
        Assert.Equal(Category, player.CategoryId);
        Assert.Equal("Club Norte", player.Club);
        Assert.True(player.IsActive);
        Assert.Equal((Now, Now), (player.CreatedAtUtc, player.UpdatedAtUtc));
    }

    [Fact]
    public void Create_BlankOptionalFields_AreStoredAsNull()
    {
        var player = Player.Create(PlayerId.New(), Organization, null, new PlayerDetails(
            "Ana", "Pérez", Email: " ", Phone: "", DocumentId: "  ", BirthDate: null, GenderId: null, CategoryId: null, Club: " "), Now).Value;

        Assert.Null(player.UserId);
        Assert.Null(player.Email);
        Assert.Null(player.Phone);
        Assert.Null(player.DocumentId);
        Assert.Null(player.Club);
        Assert.Null(player.BirthDate);
        Assert.Null(player.GenderId);
        Assert.Null(player.CategoryId);
    }

    [Fact]
    public void Create_WithoutOrganization_IsRefused()
    {
        var result = Player.Create(PlayerId.New(), new OrganizationId(Guid.Empty), null, Details(), Now);

        Assert.Equal("players.organization_id_required", result.Error.Code);
        Assert.Equal("players.errors.organization_id_required", result.Error.MessageKey);
    }

    [Theory]
    [InlineData(null, "Pérez", "players.first_name_required")]
    [InlineData("  ", "Pérez", "players.first_name_required")]
    [InlineData("Ana", null, "players.last_name_required")]
    [InlineData("Ana", " ", "players.last_name_required")]
    public void Create_MissingNames_AreRefused(string? firstName, string? lastName, string code)
    {
        Assert.Equal(code, Player.Create(PlayerId.New(), Organization, null, Details(firstName: firstName!, lastName: lastName!), Now).Error.Code);
    }

    [Fact]
    public void Create_NamesLongerThan100_AreRefused()
    {
        var limit = new string('n', 100);
        var over = new string('n', 101);

        Assert.True(Player.Create(PlayerId.New(), Organization, null, Details(firstName: limit, lastName: limit), Now).IsSuccess);
        Assert.Equal("players.name_too_long", Player.Create(PlayerId.New(), Organization, null, Details(firstName: over), Now).Error.Code);
        Assert.Equal("players.name_too_long", Player.Create(PlayerId.New(), Organization, null, Details(lastName: over), Now).Error.Code);
    }

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("@example.com")]
    [InlineData("ana@")]
    [InlineData("ana perez@example.com")]
    [InlineData("a@b@c")]
    public void Create_InvalidEmail_IsRefused(string email)
    {
        Assert.Equal("players.invalid_email", Player.Create(PlayerId.New(), Organization, null, Details(email: email), Now).Error.Code);
    }

    [Fact]
    public void Create_EmailLongerThan254_IsRefused()
    {
        var email = new string('a', 243) + "@example.com";

        Assert.Equal("players.invalid_email", Player.Create(PlayerId.New(), Organization, null, Details(email: email), Now).Error.Code);
        Assert.True(Player.Create(PlayerId.New(), Organization, null, Details(email: email[1..]), Now).IsSuccess);
    }

    [Theory]
    [InlineData(41, 0, 0)]
    [InlineData(0, 41, 0)]
    [InlineData(0, 0, 121)]
    public void Create_PhoneDocumentOrClubTooLong_IsRefused(int phone, int document, int club)
    {
        var details = Details(
            phone: phone == 0 ? "1" : new string('1', phone),
            documentId: document == 0 ? "1" : new string('1', document),
            club: club == 0 ? "c" : new string('c', club));

        Assert.Equal("players.field_too_long", Player.Create(PlayerId.New(), Organization, null, details, Now).Error.Code);
    }

    [Fact]
    public void Create_FieldsAtTheirLimits_AreAccepted()
    {
        var details = Details(phone: new string('1', 40), documentId: new string('1', 40), club: new string('c', 120));

        Assert.True(Player.Create(PlayerId.New(), Organization, null, details, Now).IsSuccess);
    }

    [Fact]
    public void Create_BirthDateAfterToday_IsRefused_TodayIsAccepted()
    {
        Assert.True(Player.Create(PlayerId.New(), Organization, null, Details(birthDate: new DateOnly(2026, 11, 10)), Now).IsSuccess);
        Assert.Equal(
            "players.birth_date_in_future",
            Player.Create(PlayerId.New(), Organization, null, Details(birthDate: new DateOnly(2026, 11, 11)), Now).Error.Code);
    }

    [Fact]
    public void Update_ReplacesTheDetailsActivityAndUser_KeepingIdentityAndCreation()
    {
        var player = Player.Create(PlayerId.New(), Organization, null, Details(), Now).Value;
        var later = Now.AddHours(2);
        var user = UserId.New();

        var result = player.Update(Details(firstName: " Bea ", email: "BEA@example.com", club: null), isActive: false, user, later);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bea", player.FirstName);
        Assert.Equal("bea@example.com", player.Email);
        Assert.Null(player.Club);
        Assert.False(player.IsActive);
        Assert.Equal(user, player.UserId);
        Assert.Equal(Organization, player.OrganizationId);
        Assert.Equal((Now, later), (player.CreatedAtUtc, player.UpdatedAtUtc));
    }

    [Fact]
    public void Update_InvalidDetails_LeaveThePlayerUnchanged()
    {
        var player = Player.Create(PlayerId.New(), Organization, null, Details(), Now).Value;

        var result = player.Update(Details(firstName: " ", email: "x@example.com"), isActive: false, null, Now.AddHours(1));

        Assert.Equal("players.first_name_required", result.Error.Code);
        Assert.Equal("Ana", player.FirstName);
        Assert.Equal("ana@example.com", player.Email);
        Assert.True(player.IsActive);
        Assert.Equal(Now, player.UpdatedAtUtc);
    }

    [Fact]
    public void Reconstitute_KeepsStoredValuesAsIs()
    {
        var id = PlayerId.New();
        var player = Player.Reconstitute(id, Organization, null, Details(firstName: "Ana"), isActive: false, Now, Now.AddDays(1));

        Assert.Equal(id, player.Id);
        Assert.False(player.IsActive);
        Assert.Equal(Now.AddDays(1), player.UpdatedAtUtc);
        Assert.Equal("ana@example.com", player.Email);
    }

    [Fact]
    public void NormalizeEmail_TrimsAndLowercases_AndBlankIsNull()
    {
        Assert.Equal("ana@example.com", Player.NormalizeEmail("  ANA@Example.com "));
        Assert.Null(Player.NormalizeEmail("  "));
        Assert.Null(Player.NormalizeEmail(null));
    }

    private static PlayerDetails Details(
        string firstName = "Ana",
        string lastName = "Pérez",
        string? email = "ana@example.com",
        string? phone = null,
        string? documentId = null,
        string? club = "Club Norte",
        DateOnly? birthDate = null,
        Guid? genderId = null,
        Guid? categoryId = null) =>
        new(firstName, lastName, email, phone, documentId,
            birthDate ?? new DateOnly(1990, 5, 1),
            genderId ?? Gender, categoryId ?? Category, club);
}
