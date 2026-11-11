using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Users;

namespace ZonarHub.Tests.Domain.Players;

/// <summary>A public registration may only complete an existing player, never overwrite it (D4).</summary>
public class PlayerFillMissingTests
{
    private static readonly DateTime Created = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly OrganizationId Organization = new(Guid.Parse("00000000-0000-0000-0000-0000000000b1"));
    private static readonly Guid Male = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid Female = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid Fourth = Guid.Parse("b1000000-0000-0000-0000-000000000004");
    private static readonly Guid Sixth = Guid.Parse("b1000000-0000-0000-0000-000000000006");

    [Fact]
    public void FillMissingFrom_FillsOnlyTheEmptyFields_AndKeepsNamesStatusAndUser()
    {
        var user = UserId.New();
        var stored = Player.Reconstitute(
            PlayerId.New(), Organization, user,
            new PlayerDetails("Ana", "Gil", "ana@club.test", null, null, null, Male, null, null),
            false, Created, Created);
        var input = Input(new PlayerDetails("Anita", "Gil Paz", "other@club.test", "+54 11", "30111222", new DateOnly(1990, 5, 1), Female, Fourth, "Norte"));

        var changed = stored.FillMissingFrom(input, Now);

        Assert.True(changed);
        Assert.Equal(("Ana", "Gil", "ana@club.test", Male), (stored.FirstName, stored.LastName, stored.Email, stored.GenderId));
        Assert.Equal(("+54 11", "30111222", new DateOnly(1990, 5, 1), Fourth, "Norte"), (stored.Phone, stored.DocumentId, stored.BirthDate, stored.CategoryId, stored.Club));
        Assert.Equal((false, user, Created, Now), (stored.IsActive, stored.UserId, stored.CreatedAtUtc, stored.UpdatedAtUtc));
    }

    [Fact]
    public void FillMissingFrom_CompletePlayer_ChangesNothing()
    {
        var details = new PlayerDetails("Ana", "Gil", "ana@club.test", "+54 11", "30111222", new DateOnly(1990, 5, 1), Male, Fourth, "Norte");
        var stored = Player.Reconstitute(PlayerId.New(), Organization, null, details, true, Created, Created);

        var changed = stored.FillMissingFrom(
            Input(new PlayerDetails("Eva", "Sol", "eva@club.test", "+54 22", "40111222", new DateOnly(2000, 1, 1), Female, Sixth, "Sur")),
            Now);

        Assert.False(changed);
        Assert.Equal(
            details,
            new PlayerDetails(stored.FirstName, stored.LastName, stored.Email, stored.Phone, stored.DocumentId, stored.BirthDate, stored.GenderId, stored.CategoryId, stored.Club));
        Assert.Equal(Created, stored.UpdatedAtUtc);
    }

    [Fact]
    public void FillMissingFrom_InputWithoutData_ChangesNothing()
    {
        var stored = Player.Reconstitute(
            PlayerId.New(), Organization, null, new PlayerDetails("Ana", "Gil", null, null, null, null, null, null, null), true, Created, Created);

        Assert.False(stored.FillMissingFrom(Input(new PlayerDetails("Ana", "Gil", null, null, null, null, null, null, null)), Now));
        Assert.Equal(Created, stored.UpdatedAtUtc);
    }

    private static Player Input(PlayerDetails details) => Player.Create(PlayerId.New(), Organization, null, details, Now).Value;
}
