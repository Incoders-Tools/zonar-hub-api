using ZonarHub.Domain.Registrations;

namespace ZonarHub.Tests.Domain.Registrations;

/// <summary>D2: public submissions are only accepted between the registration start and end dates (both inclusive).</summary>
public class RegistrationWindowTests
{
    private static readonly DateOnly Opens = new(2026, 11, 1);
    private static readonly DateOnly Closes = new(2026, 11, 15);

    [Theory]
    [InlineData("2026-10-31", RegistrationWindowState.NotStarted)]
    [InlineData("2026-11-01", RegistrationWindowState.Open)]
    [InlineData("2026-11-15", RegistrationWindowState.Open)]
    [InlineData("2026-11-16", RegistrationWindowState.Ended)]
    public void StateOn_BothDates_IsOpenFromStartToEndInclusive(string day, RegistrationWindowState expected)
    {
        var window = new RegistrationWindow(Opens, Closes);

        Assert.Equal(expected, window.StateOn(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void StateOn_MissingDate_DoesNotRestrictThatSide()
    {
        Assert.Equal(RegistrationWindowState.Open, new RegistrationWindow(null, Closes).StateOn(new DateOnly(2000, 1, 1)));
        Assert.Equal(RegistrationWindowState.Ended, new RegistrationWindow(null, Closes).StateOn(Closes.AddDays(1)));
        Assert.Equal(RegistrationWindowState.Open, new RegistrationWindow(Opens, null).StateOn(new DateOnly(2099, 1, 1)));
        Assert.Equal(RegistrationWindowState.NotStarted, new RegistrationWindow(Opens, null).StateOn(Opens.AddDays(-1)));
        Assert.Equal(RegistrationWindowState.Open, new RegistrationWindow(null, null).StateOn(new DateOnly(2026, 11, 11)));
    }
}
