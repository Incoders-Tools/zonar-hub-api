using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Application.Features.PublicRegistrations;

/// <summary>
/// The email codes of the public registration (D4), one per tournament and contact email. Separate from the sign-up
/// verification code: another cache key, another lifetime rule and single use. Only a salted SHA-256 hash of the code is
/// cached, with its expiry and failed attempts.
/// </summary>
/// <remarks>
/// A code expires after <see cref="Lifetime"/>, is removed after <see cref="MaxFailedAttempts"/> wrong attempts and once a
/// registration uses it. Another code for the same address and tournament may be requested after
/// <see cref="ResendCooldown"/>; it replaces the previous one.
/// </remarks>
internal sealed class PublicRegistrationCodes(ICacheStore cache, IClock clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    public const int MaxFailedAttempts = 5;
    public const int Length = 6;

    public static string Key(Guid tournamentId, string email) => $"public-registration-code:{tournamentId:N}:{email}";

    /// <summary>A uniformly random code of <see cref="Length"/> digits from a cryptographic source.</summary>
    public static string NewCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>False while the current code of the address was sent less than <see cref="ResendCooldown"/> ago.</summary>
    public async Task<bool> CanSendAsync(Guid tournamentId, string email, CancellationToken cancellationToken)
    {
        var entry = await cache.GetAsync<PublicRegistrationCodeEntry>(Key(tournamentId, email), cancellationToken);
        var now = clock.UtcNow;
        return entry is null || entry.ExpiresAtUtc <= now || now - entry.SentAtUtc >= ResendCooldown;
    }

    /// <summary>Caches the hash of <paramref name="code"/> for <see cref="Lifetime"/>, replacing any previous code.</summary>
    public Task StoreAsync(Guid tournamentId, string email, string code, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var entry = new PublicRegistrationCodeEntry(Hash(salt, code), salt, now, now + Lifetime, 0);
        return cache.SetAsync(Key(tournamentId, email), entry, Lifetime, cancellationToken);
    }

    /// <summary>
    /// True when <paramref name="code"/> is the current, unexpired code of the address. A wrong code counts one failed
    /// attempt (the last allowed one removes the code); an expired code is removed. The code stays valid until
    /// <see cref="ConsumeAsync"/>.
    /// </summary>
    public async Task<bool> VerifyAsync(Guid tournamentId, string email, string? code, CancellationToken cancellationToken)
    {
        var key = Key(tournamentId, email);
        var entry = await cache.GetAsync<PublicRegistrationCodeEntry>(key, cancellationToken);
        if (entry is null)
            return false;

        var now = clock.UtcNow;
        if (entry.ExpiresAtUtc <= now)
        {
            await cache.RemoveAsync(key, cancellationToken);
            return false;
        }

        var typed = code?.Trim() ?? string.Empty;
        if (typed.Length == Length && FixedTimeEquals(Hash(entry.Salt, typed), entry.Hash))
            return true;

        var failed = entry.FailedAttempts + 1;
        if (failed >= MaxFailedAttempts)
            await cache.RemoveAsync(key, cancellationToken);
        else
            await cache.SetAsync(key, entry with { FailedAttempts = failed }, entry.ExpiresAtUtc - now, cancellationToken);
        return false;
    }

    /// <summary>Removes the code so it cannot be used again.</summary>
    public Task ConsumeAsync(Guid tournamentId, string email, CancellationToken cancellationToken) =>
        cache.RemoveAsync(Key(tournamentId, email), cancellationToken);

    private static string Hash(string salt, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{code}")));

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}

/// <summary>The cached state of one code: never the code itself.</summary>
internal sealed record PublicRegistrationCodeEntry(
    string Hash,
    string Salt,
    DateTime SentAtUtc,
    DateTime ExpiresAtUtc,
    int FailedAttempts);
