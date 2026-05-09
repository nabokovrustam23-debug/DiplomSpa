using Microsoft.Extensions.Logging;

namespace BarbershopCrm.Infrastructure.Email;

/// <summary>
/// Development email sender — writes the message to the application log.
/// Replace with an SMTP-based implementation in production.
/// </summary>
public sealed class LogEmailSender : IEmailSender
{
    private readonly ILogger<LogEmailSender> _log;

    public LogEmailSender(ILogger<LogEmailSender> log) => _log = log;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _log.LogInformation(
            "[EMAIL → {To}] {Subject}{NewLine}{Body}",
            message.To, message.Subject, Environment.NewLine, message.Body);
        return Task.CompletedTask;
    }
}
