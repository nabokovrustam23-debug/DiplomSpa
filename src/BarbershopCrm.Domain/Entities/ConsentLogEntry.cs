namespace BarbershopCrm.Domain.Entities;

public class ConsentLogEntry
{
    public int ConsentId { get; set; }
    public int PersonaId { get; set; }
    public string ConsentType { get; set; } = null!;
    public DateTime AcceptedAt { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public Persona Persona { get; set; } = null!;
}
