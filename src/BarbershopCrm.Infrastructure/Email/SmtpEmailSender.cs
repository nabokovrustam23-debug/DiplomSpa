using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BarbershopCrm.Infrastructure.Email;

/// <summary>
/// Production email sender using MailKit. Activated when configuration sets
/// <c>"Email": { "Provider": "Smtp", "Smtp": { ... } }</c>. Treats SMTP errors
/// as exceptions, so the dispatcher marks the row Failed (with the error message).
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions.SmtpOptions _smtp;
    private readonly ILogger<SmtpEmailSender> _log;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> log)
    {
        _smtp = options.Value.Smtp;
        _log = log;
        if (string.IsNullOrWhiteSpace(_smtp.Host))
            throw new InvalidOperationException(
                "Email:Smtp:Host is required when Email:Provider = 'Smtp'.");
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_smtp.FromName, _smtp.FromAddress));
        msg.To.Add(MailboxAddress.Parse(message.To));
        msg.Subject = message.Subject;

        var builder = new BodyBuilder { TextBody = message.Body };
        msg.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        var secure = _smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;

        await client.ConnectAsync(_smtp.Host, _smtp.Port, secure, cancellationToken);
        if (!string.IsNullOrEmpty(_smtp.Username))
        {
            await client.AuthenticateAsync(_smtp.Username, _smtp.Password, cancellationToken);
        }
        await client.SendAsync(msg, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);

        _log.LogInformation("[SMTP → {To}] {Subject}", message.To, message.Subject);
    }
}
