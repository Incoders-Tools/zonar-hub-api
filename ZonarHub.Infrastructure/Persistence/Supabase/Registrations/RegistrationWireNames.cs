using ZonarHub.Domain.Registrations;

namespace ZonarHub.Infrastructure.Persistence.Supabase.Registrations;

/// <summary>
/// Stored spellings of the registration enums: the values the check constraints and SQL functions of migration
/// <c>20261110100000_tournament_registrations</c> accept.
/// </summary>
internal static class RegistrationWireNames
{
    private static readonly IReadOnlyDictionary<RegistrationStatus, string> Statuses = new Dictionary<RegistrationStatus, string>
    {
        [RegistrationStatus.Pending] = "pending",
        [RegistrationStatus.Confirmed] = "confirmed",
        [RegistrationStatus.Rejected] = "rejected",
        [RegistrationStatus.Waitlisted] = "waitlisted",
        [RegistrationStatus.Cancelled] = "cancelled",
    };

    private static readonly IReadOnlyDictionary<RegistrationSource, string> Sources = new Dictionary<RegistrationSource, string>
    {
        [RegistrationSource.Public] = "public",
        [RegistrationSource.Admin] = "admin",
    };

    private static readonly IReadOnlyDictionary<RegistrationPaymentStatus, string> PaymentStatuses = new Dictionary<RegistrationPaymentStatus, string>
    {
        [RegistrationPaymentStatus.NotRequired] = "not_required",
        [RegistrationPaymentStatus.Pending] = "pending",
        [RegistrationPaymentStatus.Paid] = "paid",
        [RegistrationPaymentStatus.Refunded] = "refunded",
        [RegistrationPaymentStatus.Failed] = "failed",
    };

    public static string ToWireName(this RegistrationStatus value) => Statuses[value];

    public static string ToWireName(this RegistrationSource value) => Sources[value];

    public static string ToWireName(this RegistrationPaymentStatus value) => PaymentStatuses[value];

    public static bool TryParse(string? name, out RegistrationStatus value) => TryParse(Statuses, name, out value);

    public static bool TryParse(string? name, out RegistrationSource value) => TryParse(Sources, name, out value);

    public static bool TryParse(string? name, out RegistrationPaymentStatus value) => TryParse(PaymentStatuses, name, out value);

    private static bool TryParse<T>(IReadOnlyDictionary<T, string> names, string? name, out T value)
        where T : struct
    {
        foreach (var (candidate, wire) in names)
        {
            if (string.Equals(wire, name, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }
}
