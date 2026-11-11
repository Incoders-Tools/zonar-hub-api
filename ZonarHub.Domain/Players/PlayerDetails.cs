namespace ZonarHub.Domain.Players;

/// <summary>
/// Editable data of a <see cref="Player"/>. <see cref="GenderId"/> and <see cref="CategoryId"/> reference the
/// <c>genders</c> and <c>categories</c> catalogs.
/// </summary>
public sealed record PlayerDetails(
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? DocumentId,
    DateOnly? BirthDate,
    Guid? GenderId,
    Guid? CategoryId,
    string? Club);
