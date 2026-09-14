using System.ComponentModel.DataAnnotations;

namespace AyazTekServis.Models;

public class ServiceRecord
{
    public int Id { get; set; }

    [MaxLength(200)]
    [Display(Name = "Gönderen")]
    public string? Sender { get; set; }

    [MaxLength(254), EmailAddress(ErrorMessage = "Geçerli bir müşteri e-posta adresi girin.")]
    [Display(Name = "Müşteri E-postası")]
    public string? CustomerEmail { get; set; }

    [MaxLength(24)]
    [RegularExpression(@"^\s*(?:\+?[0-9][0-9\s().-]{8,20}[0-9])?\s*$", ErrorMessage = "Telefon numarasını geçerli formatta girin veya boş bırakın.")]
    [Display(Name = "Müşteri Telefonu")]
    public string? CustomerPhone { get; set; }

    [Required(ErrorMessage = "Marka zorunludur."), MaxLength(100)]
    public string Brand { get; set; } = string.Empty;

    [Required(ErrorMessage = "Model zorunludur."), MaxLength(150)]
    public string Model { get; set; } = string.Empty;

    [Required(ErrorMessage = "Cins zorunludur."), MaxLength(100)]
    [Display(Name = "Ürün Cinsi")]
    public string ProductType { get; set; } = string.Empty;

    [Required(ErrorMessage = "Seri numarası zorunludur."), MaxLength(150)]
    [Display(Name = "Seri No")]
    public string SerialNumber { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Kayıt / Kabul Tarihi")]
    public DateTime ServiceArrivalDate { get; set; } = DateTime.Today;

    [DataType(DataType.Date)]
    [Display(Name = "Servise Gidiş Tarihi")]
    public DateTime? ServiceSentDate { get; set; }

    [Required(ErrorMessage = "Gönderilen servis zorunludur."), MaxLength(200)]
    [Display(Name = "Gönderilen Servis / Teknik Birim")]
    public string SentToService { get; set; } = string.Empty;

    [MaxLength(30)]
    [Display(Name = "Servis Takip No")]
    public string TrackingNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Arıza nedeni zorunludur."), MaxLength(4000)]
    [Display(Name = "Arıza Nedeni")]
    public string FaultReason { get; set; } = string.Empty;

    [MaxLength(2000)] public string? Accessories { get; set; }
    [MaxLength(4000)] public string? Notes { get; set; }

    [Display(Name = "Kontrol Notu"), MaxLength(4000)]
    public string? ControlNotes { get; set; }

    [Required, MaxLength(50)]
    public string Status { get; set; } = "Kayıt Açıldı";

    [MaxLength(6000)]
    public string? ServiceResult { get; set; }
    public bool IsRepaired { get; set; }

    [Display(Name = "Garanti Durumu")]
    public bool IsUnderWarranty { get; set; } = true;

    [Display(Name = "Ücret Çıktı")]
    public bool HasCharge { get; set; }

    [Range(0, 999999999)]
    [Display(Name = "Servis Ücreti")]
    public decimal? ChargeAmount { get; set; }

    [MaxLength(3)]
    [Display(Name = "Para Birimi")]
    public string ChargeCurrency { get; set; } = "TRY";

    [MaxLength(20)]
    [Display(Name = "KDV Durumu")]
    public string ChargeVatMode { get; set; } = "KDV Hariç";

    [MaxLength(50)]
    [Display(Name = "Teslim Şekli")]
    public string DeliveryMethod { get; set; } = "Müşteriye Teslim";

    [DataType(DataType.Date)]
    [Display(Name = "Teslim Tarihi")]
    public DateTime? DeliveryDate { get; set; }

    public int? CreatedByUserId { get; set; }
    [MaxLength(80)] public string CreatedByUsername { get; set; } = string.Empty;
    public int? UpdatedByUserId { get; set; }
    [MaxLength(80)] public string UpdatedByUsername { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }
    [MaxLength(80)] public string DeletedByUsername { get; set; } = string.Empty;

    public ICollection<ServicePart> Parts { get; set; } = new List<ServicePart>();
}
