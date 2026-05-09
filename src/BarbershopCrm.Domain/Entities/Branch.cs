namespace BarbershopCrm.Domain.Entities;

public class Branch
{
    public int BranchId { get; set; }
    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;
    public string? Phone { get; set; }
    public TimeOnly OpeningTime { get; set; }
    public TimeOnly ClosingTime { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Master> Masters { get; set; } = new List<Master>();
    public ICollection<User> Admins { get; set; } = new List<User>();
}
