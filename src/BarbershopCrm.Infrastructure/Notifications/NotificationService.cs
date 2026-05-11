using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BarbershopCrm.Infrastructure.Notifications;

public sealed class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<NotificationService> _log;

    public NotificationService(AppDbContext db, ILogger<NotificationService> log)
    {
        _db = db;
        _log = log;
    }

    // ---------- Booking events ----------

    public async Task OnBookingCreatedAsync(int bookingId, CancellationToken ct = default)
    {
        try
        {
            var info = await LoadBookingInfoAsync(bookingId, ct);
            if (info is null) return;

            var when = info.StartDateTime.ToString("dd.MM.yyyy в HH:mm");
            var subject = $"Запись подтверждена — {info.ServiceName}";
            var body =
                $"Здравствуйте, {info.ClientFirstName}!\n\n" +
                $"Ваша запись принята:\n" +
                $"  • Услуга: {info.ServiceName}\n" +
                $"  • Мастер: {info.MasterFullName}\n" +
                $"  • Филиал: {info.BranchName}\n" +
                $"  • Дата и время: {when}\n\n" +
                $"С нетерпением ждём вас.\n— Команда «Тихий час»";

            // Email to client (if persona has an email).
            await EnqueueEmailIfPossibleAsync(info.ClientPersonaId, subject, body, bookingId, ct);

            // In-app notification to the master.
            await EnqueueInAppAsync(info.MasterPersonaId,
                "Новая запись на ваше имя",
                $"Клиент {info.ClientFullName} записан на {when} ({info.ServiceName}, филиал {info.BranchName}).",
                bookingId, ct);

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "OnBookingCreatedAsync failed for booking {BookingId}", bookingId);
        }
    }

    public async Task OnBookingCancelledAsync(int bookingId, string? reason, CancellationToken ct = default)
    {
        try
        {
            var info = await LoadBookingInfoAsync(bookingId, ct);
            if (info is null) return;

            var when = info.StartDateTime.ToString("dd.MM.yyyy в HH:mm");
            var subject = $"Запись отменена — {info.ServiceName}";
            var reasonLine = string.IsNullOrWhiteSpace(reason) ? "" : $"Причина: {reason}\n\n";
            var body =
                $"Здравствуйте, {info.ClientFirstName}!\n\n" +
                $"Ваша запись на {when} ({info.ServiceName}, мастер {info.MasterFullName}, филиал {info.BranchName}) была отменена.\n\n" +
                reasonLine +
                $"Если это произошло по ошибке — свяжитесь с нами.\n— Команда «Тихий час»";

            await EnqueueEmailIfPossibleAsync(info.ClientPersonaId, subject, body, bookingId, ct);
            await EnqueueInAppAsync(info.MasterPersonaId,
                "Запись отменена",
                $"Запись клиента {info.ClientFullName} на {when} ({info.ServiceName}) отменена.",
                bookingId, ct);

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "OnBookingCancelledAsync failed for booking {BookingId}", bookingId);
        }
    }

    public async Task OnBookingConfirmedAsync(int bookingId, CancellationToken ct = default)
    {
        try
        {
            var info = await LoadBookingInfoAsync(bookingId, ct);
            if (info is null) return;

            var when = info.StartDateTime.ToString("dd.MM.yyyy в HH:mm");
            var subject = $"Запись подтверждена мастером — {info.ServiceName}";
            var body =
                $"Здравствуйте, {info.ClientFirstName}!\n\n" +
                $"Ваш мастер подтвердил запись на {when} ({info.ServiceName}, филиал {info.BranchName}). Ждём вас.\n\n" +
                $"— Команда «Тихий час»";

            await EnqueueEmailIfPossibleAsync(info.ClientPersonaId, subject, body, bookingId, ct);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "OnBookingConfirmedAsync failed for booking {BookingId}", bookingId);
        }
    }

    public async Task OnBookingCompletedAsync(int bookingId, CancellationToken ct = default)
    {
        try
        {
            var info = await LoadBookingInfoAsync(bookingId, ct);
            if (info is null) return;

            var when = info.StartDateTime.ToString("dd.MM.yyyy");
            await EnqueueInAppAsync(info.ClientPersonaId,
                "Спасибо за визит",
                $"Спасибо, что выбрали нас {when} ({info.ServiceName}). Будем рады видеть вас снова.",
                bookingId, ct);

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "OnBookingCompletedAsync failed for booking {BookingId}", bookingId);
        }
    }

    public async Task OnLeadCreatedAsync(int leadId, CancellationToken ct = default)
    {
        try
        {
            var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.LeadId == leadId, ct);
            if (lead is null) return;

            // Recipients: admins of the preferred branch (if specified) + the owner. Always at least one row goes out.
            var adminQuery = _db.Users.AsNoTracking().Where(u => u.IsActive && u.Role.Code == RoleCode.Admin);
            if (lead.PreferredBranchId.HasValue)
                adminQuery = adminQuery.Where(u => u.BranchId == lead.PreferredBranchId.Value);

            var adminRecipients = await adminQuery.Select(u => u.PersonaId).Distinct().ToListAsync(ct);
            var ownerRecipients = await _db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.Role.Code == RoleCode.Owner)
                .Select(u => u.PersonaId)
                .ToListAsync(ct);

            var personaIds = adminRecipients.Concat(ownerRecipients).Distinct().ToList();
            if (personaIds.Count == 0) return;

            var branchSuffix = lead.PreferredBranchId.HasValue
                ? $", предпочитаемый филиал #{lead.PreferredBranchId.Value}"
                : "";
            var commentSuffix = string.IsNullOrWhiteSpace(lead.Comment) ? "" : $"\nКомментарий: {lead.Comment}";
            var body = $"Имя: {lead.RawName}\nТелефон: {lead.RawPhone}{branchSuffix}{commentSuffix}";

            foreach (var pid in personaIds)
            {
                await EnqueueInAppAsync(pid, "Новая заявка", body, relatedBookingId: null, ct);
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "OnLeadCreatedAsync failed for lead {LeadId}", leadId);
        }
    }

    // ---------- Reading ----------

    public async Task<IReadOnlyList<Notification>> GetForRecipientAsync(int recipientPersonaId, bool unreadOnly, int take, CancellationToken ct = default)
    {
        var q = _db.Notifications.AsNoTracking()
            .Where(n => n.RecipientPersonaId == recipientPersonaId);
        if (unreadOnly) q = q.Where(n => n.ReadAt == null);
        return await q.OrderByDescending(n => n.CreatedAt)
            .Take(Math.Max(1, take))
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetForRecipientPagedAsync(
        int recipientPersonaId,
        ReadFilter filter,
        bool oldestFirst,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var q = _db.Notifications.AsNoTracking()
            .Where(n => n.RecipientPersonaId == recipientPersonaId
                     && n.Channel == NotificationChannel.InApp);
        q = filter switch
        {
            ReadFilter.Unread => q.Where(n => n.ReadAt == null),
            ReadFilter.Read   => q.Where(n => n.ReadAt != null),
            _ => q,
        };

        var total = await q.CountAsync(ct);
        var ordered = oldestFirst
            ? q.OrderBy(n => n.CreatedAt)
            : q.OrderByDescending(n => n.CreatedAt);

        var skip = Math.Max(0, (page - 1) * pageSize);
        var items = await ordered.Skip(skip).Take(Math.Max(1, pageSize)).ToListAsync(ct);
        return (items, total);
    }

    public async Task<int> GetUnreadCountAsync(int recipientPersonaId, CancellationToken ct = default)
    {
        return await _db.Notifications.AsNoTracking()
            .CountAsync(n => n.RecipientPersonaId == recipientPersonaId
                          && n.Channel == NotificationChannel.InApp
                          && n.ReadAt == null, ct);
    }

    public async Task<bool> MarkReadAsync(int notificationId, int actorPersonaId, CancellationToken ct = default)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.NotificationId == notificationId, ct);
        if (n is null) return false;
        if (n.RecipientPersonaId != actorPersonaId) return false;
        if (n.ReadAt is not null) return true;
        n.ReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> MarkAllReadAsync(int actorPersonaId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var unread = await _db.Notifications
            .Where(n => n.RecipientPersonaId == actorPersonaId && n.ReadAt == null)
            .ToListAsync(ct);
        foreach (var n in unread) n.ReadAt = now;
        if (unread.Count > 0) await _db.SaveChangesAsync(ct);
        return unread.Count;
    }

    // ---------- Helpers ----------

    private sealed record BookingInfo(
        int ClientPersonaId, string ClientFullName, string ClientFirstName,
        int MasterPersonaId, string MasterFullName,
        string ServiceName, string BranchName, DateTime StartDateTime);

    private async Task<BookingInfo?> LoadBookingInfoAsync(int bookingId, CancellationToken ct)
    {
        var data = await _db.Bookings.AsNoTracking()
            .Where(b => b.BookingId == bookingId)
            .Select(b => new
            {
                ClientPersonaId = b.Client.PersonaId,
                ClientLast = b.Client.Persona.LastName,
                ClientFirst = b.Client.Persona.FirstName,
                MasterPersonaId = b.Master.PersonaId,
                MasterLast = b.Master.Persona.LastName,
                MasterFirst = b.Master.Persona.FirstName,
                ServiceName = b.Service.Name,
                BranchName = b.Branch.Name,
                b.StartDateTime,
            })
            .FirstOrDefaultAsync(ct);
        if (data is null) return null;
        return new BookingInfo(
            data.ClientPersonaId,
            $"{data.ClientLast} {data.ClientFirst}",
            data.ClientFirst,
            data.MasterPersonaId,
            $"{data.MasterLast} {data.MasterFirst}",
            data.ServiceName,
            data.BranchName,
            data.StartDateTime);
    }

    private async Task EnqueueEmailIfPossibleAsync(int recipientPersonaId, string subject, string body,
        int? relatedBookingId, CancellationToken ct)
    {
        var hasEmail = await _db.Persona.AsNoTracking()
            .AnyAsync(p => p.PersonaId == recipientPersonaId && p.Email != null && p.Email != "", ct);
        if (!hasEmail)
        {
            // Fall back to in-app only when there's no email on file.
            await EnqueueInAppAsync(recipientPersonaId, subject, body, relatedBookingId, ct);
            return;
        }
        _db.Notifications.Add(new Notification
        {
            RecipientPersonaId = recipientPersonaId,
            Channel = NotificationChannel.Email,
            Subject = subject,
            Body = body,
            RelatedBookingId = relatedBookingId,
            Status = NotificationStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        });

        // Always also create a parallel in-app notification, so the recipient sees it
        // in the app immediately without depending on the SMTP roundtrip.
        await EnqueueInAppAsync(recipientPersonaId, subject, body, relatedBookingId, ct);
    }

    private Task EnqueueInAppAsync(int recipientPersonaId, string subject, string body,
        int? relatedBookingId, CancellationToken ct)
    {
        _db.Notifications.Add(new Notification
        {
            RecipientPersonaId = recipientPersonaId,
            Channel = NotificationChannel.InApp,
            Subject = subject,
            Body = body,
            RelatedBookingId = relatedBookingId,
            Status = NotificationStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        });
        return Task.CompletedTask;
    }
}
