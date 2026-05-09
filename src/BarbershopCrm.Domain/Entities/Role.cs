namespace BarbershopCrm.Domain.Entities;

public class Role
{
    public int RoleId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;

    public ICollection<User> Users { get; set; } = new List<User>();
}
