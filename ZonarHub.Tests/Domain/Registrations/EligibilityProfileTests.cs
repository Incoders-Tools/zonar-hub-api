using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Registrations;

namespace ZonarHub.Tests.Domain.Registrations;

/// <summary>Organization-scoped eligibility profiles and their slots (D1; the D3 checks that use them live in R2).</summary>
public class EligibilityProfileTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly OrganizationId Organization = new(Guid.Parse("00000000-0000-0000-0000-0000000000b1"));
    private static readonly Guid Female = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid Fourth = Guid.Parse("b1000000-0000-0000-0000-000000000004");

    [Fact]
    public void Create_TrimsText_OrdersSlots_AndStartsActive()
    {
        var id = EligibilityProfileId.New();

        var profile = EligibilityProfile.Create(id, Organization, "  Damas 4ta ", "  Pairs of women ", [
            new EligibilitySlot(2, Female, Fourth, 18, null, "  Player 2 ", Required: true),
            new EligibilitySlot(1, Female, null, null, 45, " ", Required: false),
        ], Now).Value;

        Assert.Equal(id, profile.Id);
        Assert.Equal(Organization, profile.OrganizationId);
        Assert.Equal("Damas 4ta", profile.Name);
        Assert.Equal("Pairs of women", profile.Description);
        Assert.True(profile.IsActive);
        Assert.Equal((Now, Now), (profile.CreatedAtUtc, profile.UpdatedAtUtc));
        Assert.Equal(
            [new EligibilitySlot(1, Female, null, null, 45, null, false), new EligibilitySlot(2, Female, Fourth, 18, null, "Player 2", true)],
            profile.Slots);
    }

    [Fact]
    public void Create_WithoutOrganization_IsRefused()
    {
        Assert.Equal(
            "eligibility_profiles.organization_id_required",
            EligibilityProfile.Create(EligibilityProfileId.New(), new OrganizationId(Guid.Empty), "P", null, [Slot(1)], Now).Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Create_WithoutName_IsRefused(string? name)
    {
        Assert.Equal("eligibility_profiles.name_required", Create(name: name!).Error.Code);
    }

    [Fact]
    public void Create_TextLimits_AreEnforced()
    {
        Assert.True(Create(name: new string('n', 120), description: new string('d', 500)).IsSuccess);
        Assert.Equal("eligibility_profiles.name_too_long", Create(name: new string('n', 121)).Error.Code);
        Assert.Equal("eligibility_profiles.description_too_long", Create(description: new string('d', 501)).Error.Code);
        Assert.Equal("eligibility_profiles.label_too_long", Create(slots: [Slot(1) with { Label = new string('l', 61) }]).Error.Code);
        Assert.True(Create(slots: [Slot(1) with { Label = new string('l', 60) }]).IsSuccess);
    }

    [Fact]
    public void Create_WithoutSlots_IsRefused()
    {
        Assert.Equal("eligibility_profiles.slots_required", Create(slots: []).Error.Code);
        Assert.Equal("eligibility_profiles.slots_required", EligibilityProfile.Create(EligibilityProfileId.New(), Organization, "P", null, null!, Now).Error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void Create_SlotNumberOutsideOneToSixteen_IsRefused(int slot)
    {
        Assert.Equal("eligibility_profiles.invalid_slot", Create(slots: [Slot(slot)]).Error.Code);
    }

    [Fact]
    public void Create_DuplicateSlotNumber_IsRefused()
    {
        Assert.Equal("eligibility_profiles.invalid_slot", Create(slots: [Slot(1), Slot(1)]).Error.Code);
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(null, 121)]
    [InlineData(40, 30)]
    public void Create_InvalidAgeRange_IsRefused(int? min, int? max)
    {
        Assert.Equal("eligibility_profiles.invalid_age_range", Create(slots: [Slot(1) with { MinAge = min, MaxAge = max }]).Error.Code);
    }

    [Theory]
    [InlineData(0, 120)]
    [InlineData(30, 30)]
    [InlineData(null, null)]
    public void Create_ValidAgeRange_IsAccepted(int? min, int? max)
    {
        Assert.True(Create(slots: [Slot(1) with { MinAge = min, MaxAge = max }]).IsSuccess);
    }

    [Fact]
    public void Update_ReplacesEverythingButIdentityAndCreation()
    {
        var profile = Create().Value;
        var later = Now.AddMinutes(5);

        var result = profile.Update(" Mixto ", null, isActive: false, [Slot(1), Slot(2)], later);

        Assert.True(result.IsSuccess);
        Assert.Equal("Mixto", profile.Name);
        Assert.Null(profile.Description);
        Assert.False(profile.IsActive);
        Assert.Equal([1, 2], profile.Slots.Select(s => s.SlotNumber));
        Assert.Equal((Now, later), (profile.CreatedAtUtc, profile.UpdatedAtUtc));
    }

    [Fact]
    public void Update_Invalid_LeavesTheProfileUnchanged()
    {
        var profile = Create().Value;

        Assert.Equal("eligibility_profiles.slots_required", profile.Update("Other", null, false, [], Now.AddDays(1)).Error.Code);
        Assert.Equal("Profile", profile.Name);
        Assert.True(profile.IsActive);
        Assert.Single(profile.Slots);
        Assert.Equal(Now, profile.UpdatedAtUtc);
    }

    [Fact]
    public void Reconstitute_KeepsStoredValues()
    {
        var id = EligibilityProfileId.New();

        var profile = EligibilityProfile.Reconstitute(id, Organization, "Stored", "d", isActive: false, [Slot(2), Slot(1)], Now, Now.AddDays(1));

        Assert.Equal(id, profile.Id);
        Assert.Equal(("Stored", "d", false), (profile.Name, profile.Description, profile.IsActive));
        Assert.Equal([1, 2], profile.Slots.Select(s => s.SlotNumber));
        Assert.Equal(Now.AddDays(1), profile.UpdatedAtUtc);
    }

    private static EligibilitySlot Slot(int number) => new(number, null, null, null, null, null, Required: true);

    private static ZonarHub.Domain.Common.Result<EligibilityProfile> Create(
        string name = "Profile",
        string? description = null,
        IReadOnlyList<EligibilitySlot>? slots = null) =>
        EligibilityProfile.Create(EligibilityProfileId.New(), Organization, name, description, slots ?? [Slot(1)], Now);
}
