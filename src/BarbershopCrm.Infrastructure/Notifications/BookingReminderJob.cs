using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BarbershopCrm.Infrastructure.Notifications;

/// <summary>
/// Scans upcoming bookings and creates reminder notifications N hours before the start.
/// Defaults: 24 h and 2 h before. Idempotent — uses a stable subject prefix per (booking, hours)
/// pair to avoid duplicate reminders even across restarts.
/// </summary>
public sealed class BookingReminderJob : BackgroundService
{
    private readonly IServiceProvider _root;
    private readonly NotificationOptions _opts;
    private readonly ILogger<BookingReminderJob> _log;

    public BookingReminderJob(
        IServiceProvider root,
        IOptions<NotificationOptions> opts,
        ILogger<BookingReminderJob> log)
    {
        _root = root;
        _opts = opts.Value;
        _log = log;
    }

    public static string SubjectPrefix(int hoursBefore) => $"[Reminder-{hoursBefore}h]";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opts.BackgroundEnabled)
        {
            _log.LogInformation("BookingReminderJob disabled by configuration.");
            return;
        }

        var delay = TimeSpan.FromMinutes(Math.Max(1, _opts.ReminderIntervalMinutes));
        _log.LogInformation(
            "BookingReminderJob started (interval={Min}min, thresholds={Thresholds}h).",
            _opts.ReminderIntervalMinutes, string.Join(",", _opts.ReminderHoursBefore));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOneTickAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "BookingReminderJob tick failed; will retry next cycle.");
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _log.LogInformation("BookingReminderJob stopped.");
    }

    /// <summary>Public for testing — processes one tick at a deterministic "now".</summary>
    public async Task<int> ProcessOneTickAsync(DateTime nowUtc, CancellationToken ct)
    {
        using var scope = _root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var window = TimeSpan.FromMinutes(Math.Max(1, _opts.ReminderIntervalMinutes));
        var totalCreated = 0;

        foreach (var hoursBefore in _opts.ReminderHoursBefore)
        {
            // Window: bookings whose StartDateTime ∈ [nowUtc + h, nowUtc + h + windowSize).
            var windowStart = nowUtc.AddHours(hoursBefore);
            var windowEnd = windowStart.Add(window);
            var prefix = SubjectPrefix(hoursBefore);

            var candidates = await db.Bookings.AsNoTracking()
                .Where(b => (b.Status == BookingStatus.Created || b.Status == BookingStatus.Confirmed)
                         && b.StartDateTime >= windowStart && b.StartDateTime < windowEnd)
                .Select(b => new
                {
                    b.BookingId,
                    ClientPersonaId = b.Client.PersonaId,
                    ClientFirst = b.Client.Persona.FirstName,
                    HasEmail = b.Client.Persona.Email != null && b.Client.Persona.Email != "",
                    ServiceName = b.Service.Name,
                    BranchName = b.Branch.Name,
                    MasterFullName = b.Master.Persona.LastName + " " + b.Master.Persona.FirstName,
                    b.StartDateTime,
                })
                .ToListAsync(ct);

            if (candidates.Count == 0) continue;

            // Idempotency: load already-sent reminder markers (by Subject prefix) for these booking ids.
            var bookingIds = candidates.Select(c => c.BookingId).ToList();
            var existing = await db.Notifications.AsNoTracking()
                .Where(n => n.RelatedBookingId != null
                         && bookingIds.Contains(n.RelatedBookingId.Value)
                         && n.Subject != null
                         && n.Subject.StartsWith(prefix))
                .Select(n => n.RelatedBookingId!.Value)
                .Distinct()
                .ToListAsync(ct);

            var alreadySent = new HashSet<int>(existing);

            foreach (var c in candidates)
            {
                if (alreadySent.Contains(c.BookingId)) continue;

                var when = c.StartDateTime.ToString("dd.MM.yyyy в HH:mm");
                var subject = $"{prefix} Напоминание: запись через {hoursBefore} ч — {c.ServiceName}";
                var body =
                    $"Здравствуйте, {c.ClientFirst}!\n\n" +
                    $"Напоминаем о записи через ~{hoursBefore} ч:\n" +
                    $"  • {c.ServiceName}\n" +
                    $"  • Мастер: {c.MasterFullName}\n" +
                    $"  • Филиал: {c.BranchName}\n" +
                    $"  • Время: {when}\n\n" +
                    $"До встречи! — Команда «Тихий час»";

                if (c.HasEmail)
                {
                    db.Notifications.Add(new Notification
                    {
                        RecipientPersonaId = c.ClientPersonaId,
                        Channel = NotificationChannel.Email,
                        Subject = subject,
                        Body = body,
                        RelatedBookingId = c.BookingId,
                        Status = NotificationStatus.Pending,
                        CreatedAt = DateTime.UtcNow,
                    });
                }
                db.Notifications.Add(new Notification
                {
                    RecipientPersonaId = c.ClientPersonaId,
                    Channel = NotificationChannel.InApp,
                    Subject = subject,
                    Body = body,
                    RelatedBookingId = c.BookingId,
                    Status = NotificationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                });
                totalCreated += c.HasEmail ? 2 : 1;
            }
        }

        if (totalCreated > 0) await db.SaveChangesAsync(ct);
        return totalCreated;
    }
}
