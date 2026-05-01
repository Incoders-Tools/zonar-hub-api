namespace ZonarHub.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>"Smtp"</summary>
    public string Provider { get; init; } = "Smtp";

    /// <summary>
    /// When true, SMTP delivery failures are logged and suppressed.
    /// Intended for local development/testing where no SMTP server is running.
    /// </summary>
    public bool SuppressDeliveryFailures { get; init; }

    public string FromAddress { get; init; } = "noreply@zonarhub.com";
    public string FromName { get; init; } = "ZonarHub";

    public SmtpSettings Smtp { get; init; } = new();

    public sealed class SmtpSettings
    {
        public string Host { get; init; } = string.Empty;
        public int Port { get; init; } = 587;
        public bool UseSsl { get; init; } = true;
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
    }
}
