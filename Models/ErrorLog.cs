using System.ComponentModel.DataAnnotations;

namespace AyazTekServis.Models;

public class ErrorLog
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int? UserId { get; set; }
    [MaxLength(80)] public string Username { get; set; } = string.Empty;
    [MaxLength(12)] public string Method { get; set; } = string.Empty;
    [MaxLength(500)] public string Path { get; set; } = string.Empty;
    [MaxLength(80)] public string IpAddress { get; set; } = string.Empty;
    [MaxLength(300)] public string ExceptionType { get; set; } = string.Empty;
    [MaxLength(2000)] public string Message { get; set; } = string.Empty;
    public string StackTrace { get; set; } = string.Empty;
}
