using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.FlyerBackgrounds;

public sealed class FlyerBackground : Entity<FlyerBackgroundId>
{
    private FlyerBackground(
        FlyerBackgroundId id,
        string name,
        string key,
        string imageUrl,
        string thumbnailUrl,
        string category,
        bool isActive,
        int sortOrder,
        DateTime createdAtUtc)
        : base(id)
    {
        Name = name;
        Key = key;
        ImageUrl = imageUrl;
        ThumbnailUrl = thumbnailUrl;
        Category = category;
        IsActive = isActive;
        SortOrder = sortOrder;
        CreatedAtUtc = createdAtUtc;
    }

    public string Name { get; private set; }
    public string Key { get; private set; }
    public string ImageUrl { get; private set; }
    public string ThumbnailUrl { get; private set; }
    public string Category { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Result<FlyerBackground> Create(
        FlyerBackgroundId id,
        string name,
        string key,
        string imageUrl,
        string thumbnailUrl,
        string category,
        bool isActive,
        int sortOrder,
        DateTime createdAtUtc)
    {
        var validation = Validate(name, key, imageUrl, category);
        if (validation.IsFailure)
        {
            return Result.Failure<FlyerBackground>(validation.Error);
        }

        return Result.Success(new FlyerBackground(
            id,
            name.Trim(),
            key.Trim().ToLowerInvariant(),
            imageUrl.Trim(),
            thumbnailUrl.Trim(),
            category.Trim().ToLowerInvariant(),
            isActive,
            sortOrder,
            createdAtUtc));
    }

    public static FlyerBackground Reconstitute(
        FlyerBackgroundId id,
        string name,
        string key,
        string imageUrl,
        string thumbnailUrl,
        string category,
        bool isActive,
        int sortOrder,
        DateTime createdAtUtc) =>
        new(id, name, key, imageUrl, thumbnailUrl, category, isActive, sortOrder, createdAtUtc);

    public Result Update(
        string name,
        string imageUrl,
        string thumbnailUrl,
        string category,
        bool isActive,
        int sortOrder)
    {
        var validation = Validate(name, Key, imageUrl, category);
        if (validation.IsFailure)
        {
            return validation;
        }

        Name = name.Trim();
        ImageUrl = imageUrl.Trim();
        ThumbnailUrl = thumbnailUrl.Trim();
        Category = category.Trim().ToLowerInvariant();
        IsActive = isActive;
        SortOrder = sortOrder;

        return Result.Success();
    }

    private static Result Validate(string name, string key, string imageUrl, string category)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(FlyerBackgroundErrors.NameRequired);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Failure(FlyerBackgroundErrors.KeyRequired);
        }

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return Result.Failure(FlyerBackgroundErrors.ImageUrlRequired);
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            return Result.Failure(FlyerBackgroundErrors.CategoryRequired);
        }

        return Result.Success();
    }
}
