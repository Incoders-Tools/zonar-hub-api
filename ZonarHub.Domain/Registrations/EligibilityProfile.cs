using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Domain.Registrations;

/// <summary>
/// Reusable participant rules of an organization (D1): one <see cref="EligibilitySlot"/> per participant. A tournament
/// may reference one profile (<c>tournaments.eligibility_profile_id</c>, same organization, checked by the API); without
/// it the tournament's own category, gender and modality apply (D3).
/// </summary>
public sealed class EligibilityProfile : Entity<EligibilityProfileId>
{
    public const int MaxNameLength = 120;
    public const int MaxDescriptionLength = 500;

    private EligibilityProfile(EligibilityProfileId id) : base(id) { }

    public OrganizationId OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>Slots ordered by slot number.</summary>
    public IReadOnlyList<EligibilitySlot> Slots { get; private set; } = [];

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static EligibilityProfile Reconstitute(
        EligibilityProfileId id,
        OrganizationId organizationId,
        string name,
        string? description,
        bool isActive,
        IReadOnlyList<EligibilitySlot> slots,
        DateTime createdAtUtc,
        DateTime updatedAtUtc) => new(id)
    {
        OrganizationId = organizationId,
        Name = name,
        Description = description,
        IsActive = isActive,
        Slots = slots.OrderBy(s => s.SlotNumber).ToList(),
        CreatedAtUtc = createdAtUtc,
        UpdatedAtUtc = updatedAtUtc,
    };

    public static Result<EligibilityProfile> Create(
        EligibilityProfileId id,
        OrganizationId organizationId,
        string name,
        string? description,
        IReadOnlyList<EligibilitySlot> slots,
        DateTime nowUtc)
    {
        if (organizationId.Value == Guid.Empty)
            return Result.Failure<EligibilityProfile>(EligibilityProfileErrors.OrganizationIdRequired);

        var validated = Validate(name, description, slots);
        if (validated.IsFailure)
            return Result.Failure<EligibilityProfile>(validated.Error);

        var (trimmedName, trimmedDescription, orderedSlots) = validated.Value;
        return Result.Success(new EligibilityProfile(id)
        {
            OrganizationId = organizationId,
            Name = trimmedName,
            Description = trimmedDescription,
            IsActive = true,
            Slots = orderedSlots,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        });
    }

    /// <summary>Replaces name, description, activity and slots; nothing changes when the input is invalid.</summary>
    public Result Update(string name, string? description, bool isActive, IReadOnlyList<EligibilitySlot> slots, DateTime nowUtc)
    {
        var validated = Validate(name, description, slots);
        if (validated.IsFailure)
            return Result.Failure(validated.Error);

        (Name, Description, Slots) = validated.Value;
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    private static Result<(string Name, string? Description, IReadOnlyList<EligibilitySlot> Slots)> Validate(
        string name,
        string? description,
        IReadOnlyList<EligibilitySlot> slots)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
            return Failure(EligibilityProfileErrors.NameRequired);

        if (trimmedName.Length > MaxNameLength)
            return Failure(EligibilityProfileErrors.NameTooLong);

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is { Length: > MaxDescriptionLength })
            return Failure(EligibilityProfileErrors.DescriptionTooLong);

        if (slots is null || slots.Count == 0)
            return Failure(EligibilityProfileErrors.SlotsRequired);

        if (slots.Any(s => s.SlotNumber is < 1 or > RegistrationParticipant.MaxSlotNumber)
            || slots.Select(s => s.SlotNumber).Distinct().Count() != slots.Count)
            return Failure(EligibilityProfileErrors.InvalidSlot);

        if (slots.Any(s => s.MinAge is < 0 or > EligibilitySlot.AgeLimit
                || s.MaxAge is < 0 or > EligibilitySlot.AgeLimit
                || s.MinAge > s.MaxAge))
            return Failure(EligibilityProfileErrors.InvalidAgeRange);

        var normalized = slots
            .Select(s => s with { Label = string.IsNullOrWhiteSpace(s.Label) ? null : s.Label.Trim() })
            .OrderBy(s => s.SlotNumber)
            .ToList();
        if (normalized.Any(s => s.Label is { Length: > EligibilitySlot.MaxLabelLength }))
            return Failure(EligibilityProfileErrors.LabelTooLong);

        return Result.Success<(string, string?, IReadOnlyList<EligibilitySlot>)>((trimmedName, trimmedDescription, normalized));

        static Result<(string, string?, IReadOnlyList<EligibilitySlot>)> Failure(Error error) =>
            Result.Failure<(string, string?, IReadOnlyList<EligibilitySlot>)>(error);
    }
}
