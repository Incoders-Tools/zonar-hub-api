using ZonarHub.Domain.Registrations;
using static ZonarHub.Domain.Registrations.RegistrationStatus;

namespace ZonarHub.Tests.Domain.Registrations;

/// <summary>
/// D2 status rules and capacity (S4). The SQL functions of migration 20261110100000_tournament_registrations enforce
/// the same table and raise the same codes, so these cases mirror supabase/tests/tournament_registrations.sql.
/// </summary>
public class RegistrationRulesTests
{
    public static TheoryData<RegistrationStatus, RegistrationStatus, bool> Transitions()
    {
        var allowed = new HashSet<(RegistrationStatus, RegistrationStatus)>
        {
            (Pending, Confirmed), (Pending, Rejected), (Pending, Waitlisted), (Pending, Cancelled),
            (Waitlisted, Confirmed), (Waitlisted, Cancelled),
            (Confirmed, Cancelled),
        };
        var data = new TheoryData<RegistrationStatus, RegistrationStatus, bool>();
        foreach (var from in Enum.GetValues<RegistrationStatus>())
            foreach (var to in Enum.GetValues<RegistrationStatus>())
                data.Add(from, to, allowed.Contains((from, to)));
        return data;
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void IsAllowedTransition_FollowsTheD2Table(RegistrationStatus from, RegistrationStatus to, bool expected)
    {
        Assert.Equal(expected, RegistrationRules.IsAllowedTransition(from, to));
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void CheckTransition_WithRoom_RefusesOnlyTransitionsOutsideTheTable(
        RegistrationStatus from, RegistrationStatus to, bool expected)
    {
        var result = RegistrationRules.CheckTransition(from, to, new RegistrationCapacity(MaxPairs: 8, Confirmed: 0, Pending: 0));

        Assert.Equal(expected, result.IsSuccess);
        if (!expected)
            Assert.Equal("registrations.invalid_transition", result.Error.Code);
    }

    [Theory]
    [InlineData(Pending)]
    [InlineData(Waitlisted)]
    public void CheckTransition_ConfirmingWhenConfirmedReachMaxPairs_IsCapacityReached(RegistrationStatus from)
    {
        var full = new RegistrationCapacity(MaxPairs: 2, Confirmed: 2, Pending: 0);

        var result = RegistrationRules.CheckTransition(from, Confirmed, full);

        Assert.True(result.IsFailure);
        Assert.Equal("registrations.capacity_reached", result.Error.Code);
        Assert.Equal("registrations.errors.capacity_reached", result.Error.MessageKey);
    }

    [Fact]
    public void CheckTransition_ConfirmingTheLastPlace_IsAllowed_AndPendingDoNotTakePlaces()
    {
        var lastPlace = new RegistrationCapacity(MaxPairs: 2, Confirmed: 1, Pending: 5);

        Assert.True(RegistrationRules.CheckTransition(Pending, Confirmed, lastPlace).IsSuccess);
    }

    [Fact]
    public void CheckTransition_OutsideTheTableWhenFull_ReportsTheTransitionFirst()
    {
        var full = new RegistrationCapacity(MaxPairs: 1, Confirmed: 1, Pending: 0);

        Assert.Equal("registrations.invalid_transition", RegistrationRules.CheckTransition(Rejected, Confirmed, full).Error.Code);
    }

    [Fact]
    public void CheckTransition_CancellingWhenFull_IsAllowed()
    {
        var full = new RegistrationCapacity(MaxPairs: 1, Confirmed: 1, Pending: 3);

        Assert.True(RegistrationRules.CheckTransition(Confirmed, Cancelled, full).IsSuccess);
        Assert.True(RegistrationRules.CheckTransition(Pending, Waitlisted, full).IsSuccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    public void Capacity_WithoutAPositiveMaxPairs_IsUnlimited(int? maxPairs)
    {
        var capacity = new RegistrationCapacity(maxPairs, Confirmed: 500, Pending: 500);

        Assert.False(capacity.IsLimited);
        Assert.True(capacity.HasConfirmedPlace);
        Assert.False(capacity.IsFullForNewEntries);
        Assert.Null(capacity.PlacesLeft);
        Assert.True(RegistrationRules.CheckTransition(Pending, Confirmed, capacity).IsSuccess);
    }

    [Theory]
    [InlineData(4, 0, 0, true, false, 4)]
    [InlineData(4, 3, 0, true, false, 1)]
    [InlineData(4, 2, 2, true, true, 2)]
    [InlineData(4, 4, 0, false, true, 0)]
    [InlineData(4, 5, 0, false, true, 0)]
    public void Capacity_CountsConfirmedForPlaces_AndConfirmedPlusPendingForNewEntries(
        int maxPairs, int confirmed, int pending, bool hasPlace, bool fullForNew, int placesLeft)
    {
        var capacity = new RegistrationCapacity(maxPairs, confirmed, pending);

        Assert.True(capacity.IsLimited);
        Assert.Equal(hasPlace, capacity.HasConfirmedPlace);
        Assert.Equal(fullForNew, capacity.IsFullForNewEntries);
        Assert.Equal(placesLeft, capacity.PlacesLeft);
    }

    [Fact]
    public void Admit_Pending_StaysPendingWhileConfirmedPlusPendingAreBelowMaxPairs()
    {
        var result = RegistrationRules.Admit(Pending, new RegistrationCapacity(MaxPairs: 3, Confirmed: 1, Pending: 1));

        Assert.Equal(Pending, result.Value);
    }

    [Fact]
    public void Admit_Pending_IsWaitlistedWhenConfirmedPlusPendingReachMaxPairs()
    {
        var result = RegistrationRules.Admit(Pending, new RegistrationCapacity(MaxPairs: 3, Confirmed: 1, Pending: 2));

        Assert.Equal(Waitlisted, result.Value);
    }

    [Fact]
    public void Admit_Confirmed_NeedsAConfirmedPlace()
    {
        Assert.Equal(Confirmed, RegistrationRules.Admit(Confirmed, new RegistrationCapacity(2, 1, 4)).Value);
        Assert.Equal("registrations.capacity_reached", RegistrationRules.Admit(Confirmed, new RegistrationCapacity(2, 2, 0)).Error.Code);
    }

    [Theory]
    [InlineData(Rejected)]
    [InlineData(Waitlisted)]
    [InlineData(Cancelled)]
    public void Admit_OtherInitialStatuses_AreInvalidTransitions(RegistrationStatus requested)
    {
        Assert.Equal("registrations.invalid_transition", RegistrationRules.Admit(requested, new RegistrationCapacity(null, 0, 0)).Error.Code);
    }

    [Theory]
    [InlineData(Pending, true)]
    [InlineData(Confirmed, true)]
    [InlineData(Waitlisted, true)]
    [InlineData(Rejected, false)]
    [InlineData(Cancelled, false)]
    public void IsActive_IsFalseOnlyForFinalStatuses(RegistrationStatus status, bool active)
    {
        Assert.Equal(active, RegistrationRules.IsActive(status));
    }

    [Theory]
    [InlineData(null, RegistrationPaymentStatus.NotRequired)]
    [InlineData("0", RegistrationPaymentStatus.NotRequired)]
    [InlineData("-1", RegistrationPaymentStatus.NotRequired)]
    [InlineData("0.01", RegistrationPaymentStatus.Pending)]
    [InlineData("15000", RegistrationPaymentStatus.Pending)]
    public void InitialPaymentStatus_IsNotRequiredWithoutAPositiveFee(string? fee, RegistrationPaymentStatus expected)
    {
        var feePerPair = fee is null ? (decimal?)null : decimal.Parse(fee, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, RegistrationRules.InitialPaymentStatus(feePerPair));
    }

    [Fact]
    public void Errors_FromCode_ResolvesEveryStoreCodeAndNothingElse()
    {
        foreach (var error in new[]
                 {
                     RegistrationErrors.CapacityReached, RegistrationErrors.PlayerAlreadyRegistered,
                     RegistrationErrors.ConcurrentModification, RegistrationErrors.InvalidTransition,
                     RegistrationErrors.TournamentNotFound, RegistrationErrors.PlayerNotFound,
                 })
        {
            Assert.Same(error, RegistrationErrors.FromStoreCode(error.Code));
        }

        Assert.Equal(
            ["registrations.capacity_reached", "registrations.player_already_registered", "registrations.concurrent_modification",
                "registrations.invalid_transition", "registrations.tournament_not_found", "registrations.player_not_found"],
            new[]
            {
                RegistrationErrors.CapacityReached, RegistrationErrors.PlayerAlreadyRegistered,
                RegistrationErrors.ConcurrentModification, RegistrationErrors.InvalidTransition,
                RegistrationErrors.TournamentNotFound, RegistrationErrors.PlayerNotFound,
            }.Select(e => e.Code));
        Assert.Null(RegistrationErrors.FromStoreCode("registrations.participants_required"));
        Assert.Null(RegistrationErrors.FromStoreCode("draws.concurrent_modification"));
        Assert.Null(RegistrationErrors.FromStoreCode(null));
    }
}
