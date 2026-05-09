namespace BarbershopCrm.Domain.Entities;

public class AuditLogEntry
{
    public int AuditId { get; set; }
    public int? UserId { get; set; }
    public string Action { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public int? EntityId { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }

    public User? User { get; set; }
}
