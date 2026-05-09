namespace BarbershopCrm.Domain.Entities;

public class Lead
{
    public int LeadId { get; set; }
    public int? PersonaId { get; set; }
    public string RawName { get; set; } = null!;
    public string RawPhone { get; set; } = null!;
    public int? PreferredBranchId { get; set; }
    public string? Comment { get; set; }
    public string Status { get; set; } = Domain.Enums.LeadStatus.New;
    public int? CreatedBookingId { get; set; }
    public int? ProcessedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }

    public Persona? Persona { get; set; }
    public Branch? PreferredBranch { get; set; }
    public Booking? CreatedBooking { get; set; }
    public User? ProcessedBy { get; set; }
}
