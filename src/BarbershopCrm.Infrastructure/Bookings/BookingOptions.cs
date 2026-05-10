namespace BarbershopCrm.Infrastructure.Bookings;

public sealed class BookingOptions
{
    public int SlotIntervalMinutes { get; set; } = 15;
    public int CancelCutoffHours { get; set; } = 2;
}
