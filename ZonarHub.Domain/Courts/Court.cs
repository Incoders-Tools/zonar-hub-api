using ZonarHub.Domain.Common;
using ZonarHub.Domain.Complexes;

namespace ZonarHub.Domain.Courts;

/// <summary>
/// Cancha dentro de un complejo.
/// </summary>
public sealed class Court : Entity<CourtId>
{
    private Court(CourtId id) : base(id) { }

    public ComplexId ComplexId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Court Reconstitute(
        CourtId id,
        ComplexId complexId,
        string name,
        bool isActive,
        DateTime createdAtUtc)
    {
        return new Court(id)
        {
            ComplexId = complexId,
            Name = name,
            IsActive = isActive,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public static Result<Court> Create(
        CourtId id,
        ComplexId complexId,
        string name,
        DateTime nowUtc)
    {
        if (complexId.Value == Guid.Empty)
            return Result.Failure<Court>(CourtErrors.ComplexIdRequired);

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Court>(CourtErrors.NameRequired);

        return Result.Success(new Court(id)
        {
            ComplexId = complexId,
            Name = name.Trim(),
            IsActive = true,
            CreatedAtUtc = nowUtc,
        });
    }
}
