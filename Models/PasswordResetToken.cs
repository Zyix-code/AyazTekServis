using System.ComponentModel.DataAnnotations;

namespace AyazTekServis.Models;

public class PasswordResetToken
{
    public long Id { get; set; }
    public int UserId { get; set; }
    [Required, MaxLength(128)] public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
