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

    public const int DefaultPageSize = 10;

    [BindProperty(SupportsGet = true)] public string? Filter { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true, Name = "page")] public int PageNumber { get; set; } = 1;

    public IReadOnlyList<Notification> Items { get; private set; } = Array.Empty<Notification>();
    public int UnreadCount { get; private set; }
    public int TotalCount { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)DefaultPageSize));
    public int CurrentPage => Math.Clamp(PageNumber, 1, TotalPages);

    public ReadFilter FilterValue => Filter?.ToLowerInvariant() switch
    {
        "unread" => ReadFilter.Unread,
        "read"   => ReadFilter.Read,
        _ => ReadFilter.All,
    };
    public bool OldestFirst => string.Equals(Sort, "oldest", StringComparison.OrdinalIgnoreCase);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();
        if (PageNumber < 1) PageNumber = 1;
        var (items, total) = await _notifications.GetForRecipientPagedAsync(
            Current.PersonaId, FilterValue, OldestFirst, PageNumber, DefaultPageSize, ct);
        Items = items;
        TotalCount = total;
        UnreadCount = await _notifications.GetUnreadCountAsync(Current.PersonaId, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostReadAsync(int id, CancellationToken ct)
    {
        if (Current is null) return Forbid();
        await _notifications.MarkReadAsync(id, Current.PersonaId, ct);
        return RedirectToPage(new { Filter, Sort, page = PageNumber });
    }

    public async Task<IActionResult> OnPostReadAllAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();
        await _notifications.MarkAllReadAsync(Current.PersonaId, ct);
        return RedirectToPage(new { Filter, Sort, page = PageNumber });
    }
}
