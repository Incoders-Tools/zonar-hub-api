namespace ZonarHub.Domain.Registrations;

/// <summary>Where a day falls in a tournament's registration window (D2).</summary>
public enum RegistrationWindowState
{
    /// <summary>Public submissions are accepted.</summary>
    Open = 1,

    /// <summary>Before the registration start date.</summary>
    NotStarted = 2,

    /// <summary>After the registration end date.</summary>
    Ended = 3,
}

/// <summary>
/// The tournament's <c>registration_start_date</c>..<c>registration_end_date</c> (D2), both inclusive calendar days. A
/// missing date does not restrict its side. The caller decides which day is today (UTC).
/// </summary>
public readonly record struct RegistrationWindow(DateOnly? OpensOn, DateOnly? ClosesOn)
{
    public RegistrationWindowState StateOn(DateOnly day) =>
        OpensOn > day ? RegistrationWindowState.NotStarted
        : ClosesOn < day ? RegistrationWindowState.Ended
        : RegistrationWindowState.Open;
}
