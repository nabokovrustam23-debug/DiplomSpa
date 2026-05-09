namespace BarbershopCrm.Domain.Entities;

public class MasterService
{
    public int MasterId { get; set; }
    public int ServiceId { get; set; }

    public Master Master { get; set; } = null!;
    public Service Service { get; set; } = null!;
}
