using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BarbershopCrm.Infrastructure.Notifications;

/// <summary>
/// Background service that polls the Notifications table and dispatches Pending rows.
///
///   • Email   → calls IEmailSender, then sets Status=Sent / Status=Failed.
///   • InApp   → no transport needed; flips Pending to Sent so the row is "delivered".
///   • Sms     → stub: marks Failed with a clear error (real SMS gateway not configured).
///
/// Failures are isolated per-row; a poison-pill row never blocks the rest of the batch.
/// </summary>
public sealed class NotificationDispatcher : BackgroundService
{
    private readonly IServiceProvider _root;
    private readonly NotificationOptions _opts;
    private readonly ILogger<NotificationDispatcher> _log;

    public NotificationDispatcher(
        IServiceProvider root,
        IOptions<NotificationOptions> opts,
        ILogger<NotificationDispatcher> log)
    {
        _root = root;
        _opts = opts.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opts.BackgroundEnabled)
        {
            _log.LogInformation("NotificationDispatcher disabled by configuration.");
            return;
        }

        _log.LogInformation(
            "NotificationDispatcher started (poll={Sec}s, batch={Batch}).",
            _opts.PollIntervalSeconds, _opts.BatchSize);

        var delay = TimeSpan.FromSeconds(Math.Max(1, _opts.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOneTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "NotificationDispatcher tick failed; will retry next cycle.");
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _log.LogInformation("NotificationDispatcher stopped.");
    }

    /// <summary>Public for testing — processes one polling cycle.</summary>
    public async Task ProcessOneTickAsync(CancellationToken ct)
    {
        using var scope = _root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var pending = await db.Notifications
            .Where(n => n.Status == NotificationStatus.Pending)
            .OrderBy(n => n.CreatedAt)
            .Take(Math.Max(1, _opts.BatchSize))
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        foreach (var n in pending)
        {
            try
            {
                if (n.Channel == NotificationChannel.Email)
                {
                    var to = await db.Persona.AsNoTracking()
                        .Where(p => p.PersonaId == n.RecipientPersonaId)
                        .Select(p => p.Email)
                        .FirstOrDefaultAsync(ct);
                    if (string.IsNullOrWhiteSpace(to))
                    {
                        n.Status = NotificationStatus.Failed;
                        n.Error = "Recipient has no email on file.";
                    }
                    else
                    {
                        await email.SendAsync(new EmailMessage(to, n.Subject ?? "(без темы)", n.Body), ct);
                        n.Status = NotificationStatus.Sent;
                        n.SentAt = DateTime.UtcNow;
                        n.Error = null;
                    }
                }
                else if (n.Channel == NotificationChannel.InApp)
                {
                    n.Status = NotificationStatus.Sent;
                    n.SentAt = DateTime.UtcNow;
                    n.Error = null;
                }
                else if (n.Channel == NotificationChannel.Sms)
                {
                    // Real SMS gateway not wired (paid). Stub: mark failed with a stable error so
                    // the row is auditable but doesn't keep retrying forever.
                    n.Status = NotificationStatus.Failed;
                    n.Error = "SMS gateway not configured (stub).";
                    _log.LogInformation(
                        "[SMS-STUB → persona {PersonaId}] {Subject}: {Body}",
                        n.RecipientPersonaId, n.Subject, n.Body);
                }
                else
                {
                    n.Status = NotificationStatus.Failed;
                    n.Error = $"Unknown channel '{n.Channel}'.";
                }
            }
            catch (Exception ex)
            {
                n.Status = NotificationStatus.Failed;
                n.Error = Truncate(ex.Message, 500);
                _log.LogWarning(ex,
                    "Notification {Id} (channel {Channel}) failed to dispatch.",
                    n.NotificationId, n.Channel);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max));
}
