namespace BarbershopCrm.Domain.Entities;

public class Visit
{
    public int VisitId { get; set; }
    public int BookingId { get; set; }
    public decimal TotalAmount { get; set; }
    public string? MasterNotes { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

    public Booking Booking { get; set; } = null!;
}
