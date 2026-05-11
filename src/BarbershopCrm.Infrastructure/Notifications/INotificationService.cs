using BarbershopCrm.Domain.Entities;

namespace BarbershopCrm.Infrastructure.Notifications;

/// <summary>
/// High-level facade used by domain services to enqueue notifications,
/// and by the user's notifications page to read / mark them read.
///
/// Enqueue methods always succeed (errors are logged and swallowed) so that
/// notification failures never break the surrounding business operation.
/// </summary>
public interface INotificationService
{
    Task OnBookingCreatedAsync(int bookingId, CancellationToken ct = default);
    Task OnBookingCancelledAsync(int bookingId, string? reason, CancellationToken ct = default);
    Task OnBookingConfirmedAsync(int bookingId, CancellationToken ct = default);
    Task OnBookingCompletedAsync(int bookingId, CancellationToken ct = default);
    Task OnLeadCreatedAsync(int leadId, CancellationToken ct = default);

    Task<IReadOnlyList<Notification>> GetForRecipientAsync(int recipientPersonaId, bool unreadOnly, int take, CancellationToken ct = default);
    Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetForRecipientPagedAsync(
        int recipientPersonaId,
        ReadFilter filter,
        bool oldestFirst,
        int page,
        int pageSize,
        CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(int recipientPersonaId, CancellationToken ct = default);
    Task<bool> MarkReadAsync(int notificationId, int actorPersonaId, CancellationToken ct = default);
    Task<int> MarkAllReadAsync(int actorPersonaId, CancellationToken ct = default);
}

public enum ReadFilter
{
    All,
    Unread,
    Read,
}
