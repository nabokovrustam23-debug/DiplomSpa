namespace BarbershopCrm.Infrastructure.Email;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(string To, string Subject, string Body);
