namespace BarbershopCrm.Domain.Entities;

public class Service
{
    public int ServiceId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<MasterService> MasterServices { get; set; } = new List<MasterService>();
}
