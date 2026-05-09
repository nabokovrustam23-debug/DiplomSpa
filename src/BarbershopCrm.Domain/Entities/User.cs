namespace BarbershopCrm.Domain.Entities;

public class User
{
    public int UserId { get; set; }
    public int PersonaId { get; set; }
    public int RoleId { get; set; }
    public int? BranchId { get; set; }

    public string Login { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string PasswordSalt { get; set; } = null!;
    public int PasswordIterations { get; set; } = 100_000;

    public bool IsEmailConfirmed { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    public Persona Persona { get; set; } = null!;
    public Role Role { get; set; } = null!;
    public Branch? Branch { get; set; }

    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
    public ICollection<UserToken> Tokens { get; set; } = new List<UserToken>();
}
