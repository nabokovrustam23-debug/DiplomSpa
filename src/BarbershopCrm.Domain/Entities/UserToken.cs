namespace BarbershopCrm.Domain.Entities;

public class UserToken
{
    public int TokenId { get; set; }
    public int UserId { get; set; }
    public string Purpose { get; set; } = null!;
    public string Token { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConsumedAt { get; set; }

    public User User { get; set; } = null!;

    public bool IsConsumable => ConsumedAt is null && ExpiresAt > DateTime.UtcNow;
}
