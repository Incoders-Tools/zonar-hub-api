namespace ZonarHub.Domain.Registrations;

/// <summary>Status of a tournament registration (D2). <see cref="RegistrationRules"/> holds the transitions.</summary>
public enum RegistrationStatus
{
    /// <summary>Submitted and waiting for the organizer.</summary>
    Pending = 1,

    /// <summary>Accepted by the organizer; takes one of the tournament's <c>max_pairs</c> places and feeds the draw.</summary>
    Confirmed = 2,

    /// <summary>Refused by the organizer. Final.</summary>
    Rejected = 3,

    /// <summary>Waiting for a place to free up.</summary>
    Waitlisted = 4,

    /// <summary>Withdrawn. Final.</summary>
    Cancelled = 5,
}

/// <summary>Who created a registration.</summary>
public enum RegistrationSource
{
    /// <summary>Anonymous submission from the public tournament page.</summary>
    Public = 1,

    /// <summary>Created by an organizer in the backoffice.</summary>
    Admin = 2,
}

/// <summary>Payment state of a registration (D2).</summary>
public enum RegistrationPaymentStatus
{
    /// <summary>The tournament has no fee.</summary>
    NotRequired = 1,

    /// <summary>A fee applies and has not been paid.</summary>
    Pending = 2,

    /// <summary>Paid online or marked as paid by the organizer.</summary>
    Paid = 3,

    /// <summary>Paid and returned.</summary>
    Refunded = 4,

    /// <summary>The payment attempt failed.</summary>
    Failed = 5,
}
