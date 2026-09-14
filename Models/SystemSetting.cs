using System.ComponentModel.DataAnnotations;

namespace AyazTekServis.Models;

public class SystemSetting
{
    [Key, MaxLength(100)]
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    [MaxLength(80)] public string UpdatedBy { get; set; } = string.Empty;
}
