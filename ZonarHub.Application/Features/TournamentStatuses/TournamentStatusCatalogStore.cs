using System.Text.Json;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.SystemSettings;

namespace ZonarHub.Application.Features.TournamentStatuses;

internal sealed record TournamentStatusCatalogItem(
    string Id,
    string Name,
    string Key,
    string? Description,
    int SortOrder,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public TournamentStatusResponse ToResponse() =>
        new(Id, Name, Key, Description, SortOrder, IsActive, CreatedAt, UpdatedAt);
}

internal static class TournamentStatusCatalogStore
{
    public const string SettingKey = "catalog.tournament_statuses";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<(SystemSetting? Setting, List<TournamentStatusCatalogItem> Items)> LoadAsync(
        ISystemSettingRepository settings,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var setting = await settings.GetByKeyAsync(
            SettingKey,
            SystemSettingScope.Global,
            tenantId: null,
            userId: null,
            cancellationToken);

        var items = DeserializeOrDefault(setting?.Value, clock.UtcNow)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (setting, items);
    }

    public static async Task<Result> SaveAsync(
        List<TournamentStatusCatalogItem> items,
        SystemSetting? existingSetting,
        ISystemSettingRepository settings,
        IUnitOfWork unitOfWork,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var serialized = Serialize(items);

        if (existingSetting is null)
        {
            var createSetting = SystemSetting.Create(
                SystemSettingId.New(),
                SettingKey,
                serialized,
                SystemSettingScope.Global,
                tenantId: null,
                userId: null,
                clock.UtcNow);

            if (createSetting.IsFailure)
            {
                return Result.Failure(createSetting.Error);
            }

            await settings.AddAsync(createSetting.Value, cancellationToken);
        }
        else
        {
            var updated = existingSetting.Update(
                serialized,
                SystemSettingScope.Global,
                tenantId: null,
                userId: null,
                clock.UtcNow);

            if (updated.IsFailure)
            {
                return Result.Failure(updated.Error);
            }

            settings.Update(existingSetting);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static IReadOnlyList<TournamentStatusCatalogItem> DeserializeOrDefault(string? value, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return BuildDefaults(nowUtc);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<TournamentStatusCatalogItem>>(value, JsonOptions);
            if (parsed is null || parsed.Count == 0)
            {
                return BuildDefaults(nowUtc);
            }

            return parsed;
        }
        catch
        {
            return BuildDefaults(nowUtc);
        }
    }

    private static string Serialize(IEnumerable<TournamentStatusCatalogItem> items)
    {
        var ordered = items
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return JsonSerializer.Serialize(ordered, JsonOptions);
    }

    private static IReadOnlyList<TournamentStatusCatalogItem> BuildDefaults(DateTime nowUtc)
    {
        return
        [
            new("ts1", "Inscripcion Abierta", "registration_open", "Periodo de inscripcion activo", 1, true, nowUtc, nowUtc),
            new("ts2", "En Curso", "in_progress", "Torneo en progreso", 2, true, nowUtc, nowUtc),
            new("ts3", "Finalizado", "finished", "Torneo completado", 3, true, nowUtc, nowUtc),
            new("ts4", "Cancelado", "cancelled", "Torneo cancelado", 4, true, nowUtc, nowUtc),
        ];
    }
}
