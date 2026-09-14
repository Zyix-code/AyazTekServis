using System.ComponentModel.DataAnnotations;

namespace AyazTekServis.Models;

public class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    [MaxLength(80)] public string Username { get; set; } = string.Empty;
    [MaxLength(80)] public string Action { get; set; } = string.Empty;
    [MaxLength(80)] public string EntityType { get; set; } = string.Empty;
    [MaxLength(80)] public string EntityId { get; set; } = string.Empty;
    [MaxLength(1000)] public string Description { get; set; } = string.Empty;
    public string OldValues { get; set; } = string.Empty;
    public string NewValues { get; set; } = string.Empty;
    [MaxLength(80)] public string IpAddress { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
