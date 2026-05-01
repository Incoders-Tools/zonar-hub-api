namespace ZonarHub.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>"InMemory" (dev) or "Smtp" (prod)</summary>
    public string Provider { get; init; } = "InMemory";

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
