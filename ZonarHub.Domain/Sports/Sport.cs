using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Sports;

/// <summary>
/// Global sport catalog entry. Sports are not tenant-scoped; tenant-specific enablement
/// is handled through the <c>OrganizationSport</c> assignment.
/// </summary>
public sealed class Sport : Entity<SportId>
{
    private readonly List<Guid> _modalityIds = [];

    private Sport(SportId id) : base(id) { }

    public string Name { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string Icon { get; private set; } = string.Empty;
    public SportIconSource IconSource { get; private set; }
    public IReadOnlyList<Guid> ModalityIds => _modalityIds.AsReadOnly();
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<Sport> Create(
        SportId id,
        string name,
        string key,
        string icon,
        SportIconSource iconSource,
        IEnumerable<Guid>? modalityIds,
        int sortOrder,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Sport>(SportErrors.NameRequired);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Failure<Sport>(SportErrors.KeyRequired);
        }

        if (string.IsNullOrWhiteSpace(icon))
        {
            return Result.Failure<Sport>(SportErrors.IconRequired);
        }

        var modalityList = modalityIds?.Distinct().ToList() ?? [];
        if (modalityList.Count == 0)
        {
            return Result.Failure<Sport>(SportErrors.ModalityRequired);
        }

        var sport = new Sport(id)
        {
            Name = name.Trim(),
            Key = key.Trim().ToLowerInvariant(),
            Icon = icon.Trim(),
            IconSource = iconSource,
            SortOrder = sortOrder,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        sport._modalityIds.AddRange(modalityList);

        return Result.Success(sport);
    }

    public static Sport Reconstitute(
        SportId id,
        string name,
        string key,
        string icon,
        SportIconSource iconSource,
        IEnumerable<Guid>? modalityIds,
        int sortOrder,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        var sport = new Sport(id)
        {
            Name = name,
            Key = key,
            Icon = icon,
            IconSource = iconSource,
            SortOrder = sortOrder,
            IsActive = isActive,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
        };

        if (modalityIds is not null)
        {
            sport._modalityIds.AddRange(modalityIds.Distinct());
        }

        return sport;
    }

    public Result Update(
        string name,
        string icon,
        SportIconSource iconSource,
        IEnumerable<Guid>? modalityIds,
        int sortOrder,
        bool isActive,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(SportErrors.NameRequired);
        }

        if (string.IsNullOrWhiteSpace(icon))
        {
            return Result.Failure(SportErrors.IconRequired);
        }

        var modalityList = modalityIds?.Distinct().ToList() ?? [];
        if (modalityList.Count == 0)
        {
            return Result.Failure(SportErrors.ModalityRequired);
        }

        Name = name.Trim();
        Icon = icon.Trim();
        IconSource = iconSource;
        SortOrder = sortOrder;
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;

        _modalityIds.Clear();
        _modalityIds.AddRange(modalityList);

        return Result.Success();
    }
}
