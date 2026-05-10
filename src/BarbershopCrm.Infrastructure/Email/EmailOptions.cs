namespace BarbershopCrm.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// "Log" (default — writes to Serilog) or "Smtp" (sends via MailKit).
    /// SMTP credentials live under <see cref="Smtp"/>.
    /// </summary>
    public string Provider { get; set; } = "Log";

    public SmtpOptions Smtp { get; set; } = new();

    public sealed class SmtpOptions
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromAddress { get; set; } = "no-reply@example.com";
        public string FromName { get; set; } = "Тихий час";
        public bool UseStartTls { get; set; } = true;
    }
}
