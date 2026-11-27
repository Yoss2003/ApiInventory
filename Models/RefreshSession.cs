namespace InventoryAPI.Models;

public class RefreshSession
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public int CompanyId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool RememberMe { get; set; }
}
