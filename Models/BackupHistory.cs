using System.ComponentModel.DataAnnotations;

namespace AyazTekServis.Models;

public class BackupHistory
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int? UserId { get; set; }
    [MaxLength(80)] public string Username { get; set; } = string.Empty;
    [MaxLength(260)] public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool IsSuccessful { get; set; }
    [MaxLength(1000)] public string ErrorMessage { get; set; } = string.Empty;
}
