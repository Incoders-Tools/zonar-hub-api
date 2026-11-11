using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Registrations;

/// <summary>The player data of one filled slot that the D3 rules read.</summary>
public sealed record EligibilityCandidate(int SlotNumber, Guid? GenderId, Guid? CategoryId, DateOnly? BirthDate);

/// <summary>
/// Catalog facts the D3 rules need: the level of every category and the genders that do not restrict a slot (the
/// <c>mixed</c> gender of a mixed tournament admits any player).
/// </summary>
public sealed record EligibilityCatalog(
    IReadOnlyDictionary<Guid, int> CategoryLevels,
    IReadOnlySet<Guid> UnrestrictedGenderIds);

/// <summary>
/// D3 server-side eligibility of a registration's participants against participant slots: those of the tournament's
/// eligibility profile, else <see cref="TournamentSlots"/> from the tournament's own modality, gender and category. The
/// frontend <c>EligibilityValidationService</c> mirrors these rules.
/// </summary>
/// <remarks>
/// Every filled slot must exist in the rules and every required slot must be filled. Per slot, in slot order: a gender
/// rule needs the same gender; a category rule needs a player category at most <see cref="CategoryTolerance"/> level away;
/// an age rule needs a birth date whose whole years on <c>today</c> fall in the range. Missing player data never proves
/// eligibility, so it fails the rule it is needed for.
/// </remarks>
public static class RegistrationEligibility
{
    /// <summary>Levels a player's category may differ from the required one (the existing frontend default).</summary>
    public const int CategoryTolerance = 1;

    /// <summary>
    /// Slots of a tournament without profile: <c>single</c> one required slot, <c>teams</c> up to
    /// <see cref="RegistrationParticipant.MaxSlotNumber"/> optional slots, any other modality (doubles, none or unknown) two
    /// required slots; each with the tournament's gender and category and no age limits.
    /// </summary>
    public static IReadOnlyList<EligibilitySlot> TournamentSlots(string? modalityKey, Guid? genderId, Guid? categoryId)
    {
        var (count, required) = modalityKey switch
        {
            "single" => (1, true),
            "teams" => (RegistrationParticipant.MaxSlotNumber, false),
            _ => (2, true),
        };
        return Enumerable.Range(1, count)
            .Select(slot => new EligibilitySlot(slot, genderId, categoryId, null, null, null, required))
            .ToList();
    }

    /// <summary>
    /// Success when the candidates satisfy the slots; otherwise the first failure: <see cref="RegistrationErrors.InvalidSlot"/>,
    /// <see cref="RegistrationErrors.RequiredSlotMissing"/>, then per slot <see cref="RegistrationErrors.GenderMismatch"/>,
    /// <see cref="RegistrationErrors.CategoryOutOfRange"/> or <see cref="RegistrationErrors.AgeOutOfRange"/>.
    /// </summary>
    public static Result Check(
        IReadOnlyList<EligibilitySlot> slots,
        IReadOnlyList<EligibilityCandidate> candidates,
        EligibilityCatalog catalog,
        DateOnly today)
    {
        var rules = slots.ToDictionary(slot => slot.SlotNumber);
        if (candidates.Any(candidate => !rules.ContainsKey(candidate.SlotNumber)))
            return Result.Failure(RegistrationErrors.InvalidSlot);

        var filled = candidates.Select(candidate => candidate.SlotNumber).ToHashSet();
        if (slots.Any(slot => slot.Required && !filled.Contains(slot.SlotNumber)))
            return Result.Failure(RegistrationErrors.RequiredSlotMissing);

        foreach (var candidate in candidates.OrderBy(candidate => candidate.SlotNumber))
        {
            var slot = rules[candidate.SlotNumber];
            if (slot.GenderId is { } gender && !catalog.UnrestrictedGenderIds.Contains(gender) && candidate.GenderId != gender)
                return Result.Failure(RegistrationErrors.GenderMismatch);

            if (slot.CategoryId is { } category && !IsWithinTolerance(candidate.CategoryId, category, catalog))
                return Result.Failure(RegistrationErrors.CategoryOutOfRange);

            if ((slot.MinAge is not null || slot.MaxAge is not null) && !IsAgeInRange(candidate.BirthDate, slot, today))
                return Result.Failure(RegistrationErrors.AgeOutOfRange);
        }

        return Result.Success();
    }

    /// <summary>Whole years from <paramref name="birthDate"/> to <paramref name="day"/>.</summary>
    public static int AgeOn(DateOnly birthDate, DateOnly day)
    {
        var age = day.Year - birthDate.Year;
        return birthDate.AddYears(age) > day ? age - 1 : age;
    }

    private static bool IsWithinTolerance(Guid? playerCategory, Guid requiredCategory, EligibilityCatalog catalog) =>
        playerCategory is { } category
        && catalog.CategoryLevels.TryGetValue(category, out var playerLevel)
        && catalog.CategoryLevels.TryGetValue(requiredCategory, out var requiredLevel)
        && Math.Abs(playerLevel - requiredLevel) <= CategoryTolerance;

    private static bool IsAgeInRange(DateOnly? birthDate, EligibilitySlot slot, DateOnly today)
    {
        if (birthDate is not { } born)
            return false;

        var age = AgeOn(born, today);
        return (slot.MinAge is null || age >= slot.MinAge) && (slot.MaxAge is null || age <= slot.MaxAge);
    }
}
