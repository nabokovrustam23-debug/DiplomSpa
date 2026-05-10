namespace BarbershopCrm.Domain.Entities;

public class Notification
{
    public int NotificationId { get; set; }
    public int RecipientPersonaId { get; set; }
    public string Channel { get; set; } = null!;
    public string? Subject { get; set; }
    public string Body { get; set; } = null!;
    public int? RelatedBookingId { get; set; }
    public string Status { get; set; } = Domain.Enums.NotificationStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public string? Error { get; set; }

    public Persona Recipient { get; set; } = null!;
    public Booking? RelatedBooking { get; set; }
}
