using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Notifications;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;

namespace BarbershopCrm.Web.Pages.Account.Notifications;

[AuthorizePage]
public class IndexModel : AppPageModel
{
    private readonly INotificationService _notifications;

    public IndexModel(ICurrentUserAccessor cu, INotificationService notifications) : base(cu)
    {
        _notifications = notifications;
    }

    public IReadOnlyList<Notification> Items { get; private set; } = Array.Empty<Notification>();
    public int UnreadCount { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();
        Items = await _notifications.GetForRecipientAsync(Current.PersonaId, unreadOnly: false, take: 200, ct);
        // Show only InApp; emails appear in the user's mailbox.
        Items = Items.Where(n => n.Channel == Domain.Enums.NotificationChannel.InApp).ToList();
        UnreadCount = await _notifications.GetUnreadCountAsync(Current.PersonaId, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostReadAsync(int id, CancellationToken ct)
    {
        if (Current is null) return Forbid();
        await _notifications.MarkReadAsync(id, Current.PersonaId, ct);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReadAllAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();
        await _notifications.MarkAllReadAsync(Current.PersonaId, ct);
        return RedirectToPage();
    }
}
