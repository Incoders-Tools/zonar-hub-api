using ZonarHub.Domain.Draws;

namespace ZonarHub.Tests.Domain.Draws;

public class MatchTests
{
    [Fact]
    public void IsBye_IsTrueWhenEitherSideIsABye()
    {
        var player = new ParticipantSlot("p1");

        Assert.True(new Match("M", 1, 1, player, Slot.Bye).IsBye);
        Assert.True(new Match("M", 1, 1, Slot.Bye, player).IsBye);
        Assert.False(new Match("M", 1, 1, player, new WinnerOfSlot("X")).IsBye);
        Assert.False(new Match("M", 1, 1, new GroupPositionSlot("A", 1), new LoserOfSlot("X")).IsBye);
    }
}
