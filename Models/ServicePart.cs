using System.ComponentModel.DataAnnotations;

namespace AyazTekServis.Models;

public class ServicePart
{
    public int Id { get; set; }
    public int ServiceRecordId { get; set; }

    [Required, MaxLength(20)]
    [Display(Name = "İşlem")]
    public string PartType { get; set; } = "Takılan";

    [Required(ErrorMessage = "Parça adı zorunludur."), MaxLength(200)]
    [Display(Name = "Parça Adı")]
    public string PartName { get; set; } = string.Empty;

    [MaxLength(150)]
    [Display(Name = "Parça Seri No")]
    public string SerialNumber { get; set; } = string.Empty;

    [Range(1, 9999)]
    [Display(Name = "Adet")]
    public int Quantity { get; set; } = 1;

    [Range(0, 999999999)]
    [Display(Name = "Birim Fiyat")]
    public decimal? UnitPrice { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "TRY";

    [MaxLength(500)]
    public string Notes { get; set; } = string.Empty;

    public ServiceRecord? ServiceRecord { get; set; }
}
