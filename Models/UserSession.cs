using System.ComponentModel.DataAnnotations;

namespace AyazTekServis.Models;

public class UserSession
{
    public long Id { get; set; }
    public int UserId { get; set; }
    [MaxLength(80)] public string Username { get; set; } = string.Empty;
    [MaxLength(80)] public string SessionId { get; set; } = string.Empty;
    [MaxLength(80)] public string IpAddress { get; set; } = string.Empty;
    [MaxLength(600)] public string UserAgent { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime LastSeenAt { get; set; } = DateTime.Now;
    public DateTime ExpiresAt { get; set; } = DateTime.Now.AddHours(8);
    public DateTime? RevokedAt { get; set; }
    public bool IsActive => RevokedAt == null && ExpiresAt > DateTime.Now;
}
