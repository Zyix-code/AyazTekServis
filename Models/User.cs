using System.ComponentModel.DataAnnotations;

namespace AyazTekServis.Models;

public class User
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Username { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(180)]
    public string Email { get; set; } = string.Empty;
    [Required]
    public string Password { get; set; } = string.Empty;
    [MaxLength(300)] public string SecurityQuestion { get; set; } = string.Empty;
    [MaxLength(500)] public string SecurityAnswer { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastLoginAt { get; set; }

    [MaxLength(10)]
    public string ThemePreference { get; set; } = "system";
}
