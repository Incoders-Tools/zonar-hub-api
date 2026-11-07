using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.PublicTournaments.GetAll;
using ZonarHub.Application.Features.PublicTournaments.GetBySlug;
using ZonarHub.Application.Features.PublicTournaments.GetDraw;
using ZonarHub.Domain.Organizations;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Application.TournamentDraws;

namespace ZonarHub.Tests.Application.PublicTournaments;

/// <summary>
/// Wires the real public tournament handlers to a store fake (which, like the Supabase repository, only ever holds
/// showcase tournaments) and in-memory catalogs.
/// </summary>
internal sealed class PublicTournamentsTestHarness
{
    public static readonly Guid CategoryId = Guid.NewGuid();
    public static readonly Guid GenderId = Guid.NewGuid();
    public static readonly Guid ModalityId = Guid.NewGuid();

    public PublicTournamentsTestHarness()
    {
        Categories.Items.Add(new CategoryDto(CategoryId, "Cuarta categoría", "4ta", "cuarta", 4, true, 1, Stamp, Stamp));
        Genders.Items.Add(new GenderDto(GenderId, "Mixto", "mixed", true, 1, Stamp, Stamp));
        Modalities.Items.Add(new TournamentModalityDto(ModalityId, "Parejas", "Pairs", "Duplas", "pairs", 1, true));
        Statuses.AddAsync(new TournamentStatusDto(
            Guid.NewGuid(), "registration_open", "Inscripción abierta", "Registration open", "Inscrição aberta",
            null, null, null, 1, true, Stamp, Stamp)).GetAwaiter().GetResult();

        List = new GetPublicTournamentsHandler(Tournaments, Categories, Genders, Modalities, Statuses);
        BySlug = new GetPublicTournamentBySlugHandler(Tournaments, Categories, Genders, Modalities, Statuses);
        Draw = new GetPublicTournamentDrawHandler(Tournaments, Draws);
    }

    private static readonly DateTime Stamp = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    public FakePublicTournamentReadRepository Tournaments { get; } = new();
    public FakeCategoryRepository Categories { get; } = new();
    public FakeGenderRepository Genders { get; } = new();
    public FakeModalityRepository Modalities { get; } = new();
    public InMemoryTournamentStatusRepository Statuses { get; } = new();
    public InMemoryTournamentDrawRepository Draws { get; } = new(Stamp);

    public GetPublicTournamentsHandler List { get; }
    public GetPublicTournamentBySlugHandler BySlug { get; }
    public GetPublicTournamentDrawHandler Draw { get; }

    public static OrganizationPublicProfile Consent(bool enabled = true) => OrganizationPublicProfile.Create(
        enabled,
        "https://club.example",
        "https://instagram.com/club",
        "  ",
        null,
        null,
        "+5491122334455",
        "hola@club.example");

    public static PublicOrganizationRecord Organization(OrganizationPublicProfile profile, bool isActive = true) =>
        new("Club Norte", "https://cdn.example/logo.png", "Padel club", "circuito", isActive, profile);

    public static PublicTournamentRecord Record(
        string slug = "open-norte-abc123",
        DateOnly? start = null,
        string? status = "registration_open",
        PublicOrganizationRecord? organization = null,
        Guid? id = null) => new(
        id ?? Guid.NewGuid(),
        slug,
        "Open Norte",
        "Summer open",
        "Best of three",
        start ?? new DateOnly(2026, 11, 20),
        (start ?? new DateOnly(2026, 11, 20)).AddDays(2),
        new DateOnly(2026, 11, 1),
        new DateOnly(2026, 11, 15),
        status,
        CategoryId,
        GenderId,
        ModalityId,
        32,
        "https://cdn.example/t.png",
        "https://cdn.example/cover.png",
        25000m,
        500000m,
        new PublicSportRecord("padel", "Pádel", "/uploads/sports-icons/padel.svg", "svg"),
        new PublicComplexRecord("Complejo Norte", "Av. Siempre Viva 742"),
        organization);
}

internal sealed class FakePublicTournamentReadRepository : IPublicTournamentReadRepository
{
    public List<PublicTournamentRecord> Items { get; } = [];
    public List<int> ListLimits { get; } = [];
    public List<string> SlugLookups { get; } = [];

    public Task<IReadOnlyList<PublicTournamentRecord>> ListAsync(int limit, CancellationToken cancellationToken = default)
    {
        ListLimits.Add(limit);
        IReadOnlyList<PublicTournamentRecord> page = Items.Take(limit).ToList();
        return Task.FromResult(page);
    }

    public Task<PublicTournamentRecord?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        SlugLookups.Add(slug);
        return Task.FromResult(Items.FirstOrDefault(t => t.Slug == slug));
    }
}

internal sealed class FakeCategoryRepository : ICategoryRepository
{
    public List<CategoryDto> Items { get; } = [];
    public int Reads { get; private set; }

    public Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult<IReadOnlyList<CategoryDto>>(Items);
    }

    public Task<CategoryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CategoryDto> AddAsync(CategoryDto category, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CategoryDto?> UpdateAsync(Guid id, CategoryDto category, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class FakeGenderRepository : IGenderRepository
{
    public List<GenderDto> Items { get; } = [];

    public Task<IReadOnlyList<GenderDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GenderDto>>(Items);

    public Task<GenderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<GenderDto> AddAsync(GenderDto gender, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<GenderDto?> UpdateAsync(Guid id, GenderDto gender, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class FakeModalityRepository : ITournamentModalityRepository
{
    public List<TournamentModalityDto> Items { get; } = [];

    public Task<IReadOnlyList<TournamentModalityDto>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TournamentModalityDto>>(Items.Where(m => includeInactive || m.IsActive).ToList());

    public Task<IReadOnlyList<TournamentModalityDto>> GetAllActiveAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TournamentModalityDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TournamentModalityDto?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TournamentModalityDto> AddAsync(TournamentModalityDto modality, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TournamentModalityDto> UpdateAsync(TournamentModalityDto modality, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
