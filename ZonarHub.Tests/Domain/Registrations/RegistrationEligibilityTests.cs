using ZonarHub.Domain.Registrations;

namespace ZonarHub.Tests.Domain.Registrations;

/// <summary>
/// D3 server-side eligibility: participants per modality, required and unknown slots, gender, category within one level
/// and age in whole years, checked slot by slot.
/// </summary>
public class RegistrationEligibilityTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);
    private static readonly Guid Male = Guid.NewGuid();
    private static readonly Guid Female = Guid.NewGuid();
    private static readonly Guid Mixed = Guid.NewGuid();
    private static readonly Guid Third = Guid.NewGuid();
    private static readonly Guid Fourth = Guid.NewGuid();
    private static readonly Guid Fifth = Guid.NewGuid();

    private static readonly EligibilityCatalog Catalog = new(
        new Dictionary<Guid, int> { [Third] = 3, [Fourth] = 4, [Fifth] = 5 },
        new HashSet<Guid> { Mixed });

    [Theory]
    [InlineData("single", 1, true)]
    [InlineData("doubles", 2, true)]
    [InlineData(null, 2, true)]
    [InlineData("unknown", 2, true)]
    [InlineData("teams", RegistrationParticipant.MaxSlotNumber, false)]
    public void TournamentSlots_FollowTheModality(string? modalityKey, int count, bool required)
    {
        var slots = RegistrationEligibility.TournamentSlots(modalityKey, Male, Fourth);

        Assert.Equal(Enumerable.Range(1, count), slots.Select(s => s.SlotNumber));
        Assert.All(slots, slot =>
        {
            Assert.Equal(Male, slot.GenderId);
            Assert.Equal(Fourth, slot.CategoryId);
            Assert.Null(slot.MinAge);
            Assert.Null(slot.MaxAge);
            Assert.Equal(required, slot.Required);
        });
    }

    [Fact]
    public void Check_EligiblePair_Succeeds()
    {
        var slots = RegistrationEligibility.TournamentSlots("doubles", Male, Fourth);

        var result = RegistrationEligibility.Check(
            slots, [Candidate(1, Male, Third), Candidate(2, Male, Fifth)], Catalog, Today);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Check_MissingRequiredSlot_IsRefused()
    {
        var slots = RegistrationEligibility.TournamentSlots("doubles", null, null);

        var result = RegistrationEligibility.Check(slots, [Candidate(1)], Catalog, Today);

        Assert.Equal(RegistrationErrors.RequiredSlotMissing, result.Error);
    }

    [Fact]
    public void Check_OptionalSlotsMayStayEmpty()
    {
        var slots = RegistrationEligibility.TournamentSlots("teams", null, null);

        Assert.True(RegistrationEligibility.Check(slots, [Candidate(1), Candidate(4)], Catalog, Today).IsSuccess);
    }

    [Fact]
    public void Check_SlotOutsideTheRules_IsRefused()
    {
        var slots = RegistrationEligibility.TournamentSlots("single", null, null);

        var result = RegistrationEligibility.Check(slots, [Candidate(1), Candidate(2)], Catalog, Today);

        Assert.Equal(RegistrationErrors.InvalidSlot, result.Error);
    }

    [Theory]
    [InlineData("female")]
    [InlineData("none")]
    public void Check_GenderOtherThanTheSlots_IsRefused(string playerGender)
    {
        var slots = RegistrationEligibility.TournamentSlots("single", Male, null);
        var gender = playerGender == "female" ? Female : (Guid?)null;

        var result = RegistrationEligibility.Check(slots, [Candidate(1, gender)], Catalog, Today);

        Assert.Equal(RegistrationErrors.GenderMismatch, result.Error);
    }

    [Fact]
    public void Check_MixedGender_DoesNotRestrictEachPlayer()
    {
        var slots = RegistrationEligibility.TournamentSlots("doubles", Mixed, null);

        var result = RegistrationEligibility.Check(slots, [Candidate(1, Male), Candidate(2, Female)], Catalog, Today);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("third", true)]
    [InlineData("fourth", true)]
    [InlineData("fifth", true)]
    [InlineData("none", false)]
    [InlineData("unknown", false)]
    public void Check_Category_AllowsOneLevelOfTolerance(string playerCategory, bool eligible)
    {
        var slots = RegistrationEligibility.TournamentSlots("single", null, Fourth);
        Guid? category = playerCategory switch
        {
            "third" => Third,
            "fourth" => Fourth,
            "fifth" => Fifth,
            "unknown" => Guid.NewGuid(),
            _ => null,
        };

        var result = RegistrationEligibility.Check(slots, [Candidate(1, category: category)], Catalog, Today);

        Assert.Equal(eligible, result.IsSuccess);
        if (!eligible)
            Assert.Equal(RegistrationErrors.CategoryOutOfRange, result.Error);
    }

    [Fact]
    public void Check_CategoryTwoLevelsAway_IsRefused()
    {
        var second = Guid.NewGuid();
        var catalog = Catalog with { CategoryLevels = new Dictionary<Guid, int> { [second] = 2, [Fourth] = 4 } };
        var slots = RegistrationEligibility.TournamentSlots("single", null, Fourth);

        var result = RegistrationEligibility.Check(slots, [Candidate(1, category: second)], catalog, Today);

        Assert.Equal(RegistrationErrors.CategoryOutOfRange, result.Error);
    }

    [Theory]
    [InlineData("2008-11-11", true)]   // 18 today
    [InlineData("2008-11-12", false)]  // 17, turns 18 tomorrow
    [InlineData("1986-11-12", true)]   // 39
    [InlineData("1986-11-11", false)]  // 40 today
    [InlineData(null, false)]          // no birth date: the age cannot be proven
    public void Check_Age_IsWholeYearsOnTheDay(string? birthDate, bool eligible)
    {
        EligibilitySlot[] slots = [new(1, null, null, MinAge: 18, MaxAge: 39, Label: null, Required: true)];
        var born = birthDate is null ? (DateOnly?)null : DateOnly.Parse(birthDate, System.Globalization.CultureInfo.InvariantCulture);

        var result = RegistrationEligibility.Check(slots, [Candidate(1, birthDate: born)], Catalog, Today);

        Assert.Equal(eligible, result.IsSuccess);
        if (!eligible)
            Assert.Equal(RegistrationErrors.AgeOutOfRange, result.Error);
    }

    [Fact]
    public void Check_EachSlotUsesItsOwnRules()
    {
        EligibilitySlot[] slots =
        [
            new(1, Male, null, null, null, "Drive", true),
            new(2, Female, null, null, null, "Revés", true),
        ];

        Assert.True(RegistrationEligibility.Check(slots, [Candidate(1, Male), Candidate(2, Female)], Catalog, Today).IsSuccess);
        Assert.Equal(
            RegistrationErrors.GenderMismatch,
            RegistrationEligibility.Check(slots, [Candidate(1, Female), Candidate(2, Male)], Catalog, Today).Error);
    }

    private static EligibilityCandidate Candidate(
        int slot,
        Guid? gender = null,
        Guid? category = null,
        DateOnly? birthDate = null) => new(slot, gender, category, birthDate);
}

/// <summary>Manual payment marks of the backoffice (D5): cash payments are marked paid, then refunded.</summary>
public class RegistrationManualPaymentTests
{
    [Theory]
    [InlineData(RegistrationPaymentStatus.Pending, RegistrationPaymentStatus.Paid)]
    [InlineData(RegistrationPaymentStatus.Failed, RegistrationPaymentStatus.Paid)]
    [InlineData(RegistrationPaymentStatus.Paid, RegistrationPaymentStatus.Refunded)]
    public void CheckManualPayment_AllowsPaidAndRefunded(RegistrationPaymentStatus from, RegistrationPaymentStatus to)
    {
        Assert.True(RegistrationRules.CheckManualPayment(from, to).IsSuccess);
    }

    [Theory]
    [InlineData(RegistrationPaymentStatus.NotRequired, RegistrationPaymentStatus.Paid)]
    [InlineData(RegistrationPaymentStatus.Paid, RegistrationPaymentStatus.Paid)]
    [InlineData(RegistrationPaymentStatus.Refunded, RegistrationPaymentStatus.Paid)]
    [InlineData(RegistrationPaymentStatus.Pending, RegistrationPaymentStatus.Refunded)]
    [InlineData(RegistrationPaymentStatus.Refunded, RegistrationPaymentStatus.Refunded)]
    public void CheckManualPayment_OtherOrigins_AreConflicts(RegistrationPaymentStatus from, RegistrationPaymentStatus to)
    {
        Assert.Equal(RegistrationErrors.InvalidPaymentTransition, RegistrationRules.CheckManualPayment(from, to).Error);
    }

    [Theory]
    [InlineData(RegistrationPaymentStatus.Pending)]
    [InlineData(RegistrationPaymentStatus.Failed)]
    [InlineData(RegistrationPaymentStatus.NotRequired)]
    public void CheckManualPayment_OnlyPaidOrRefundedCanBeMarked(RegistrationPaymentStatus to)
    {
        Assert.Equal(
            RegistrationErrors.InvalidPaymentStatus,
            RegistrationRules.CheckManualPayment(RegistrationPaymentStatus.Paid, to).Error);
    }
}
