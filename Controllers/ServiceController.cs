using System.Globalization;
using System.Security.Claims;
using AyazTekServis.Models;
using AyazTekServis.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AyazTekServis.Controllers;

[Authorize]
public class ServiceController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ServiceNumberGenerator _numbers;
    private readonly AuditService _audit;
    private readonly ILogger<ServiceController> _logger;
    private readonly BusinessDayService _businessDays;
    private readonly EmailService _email;

    private static readonly string[] AllowedStatuses = [
        "Kayıt Açıldı", "Serviste", "Onarımda", "Parça Bekliyor", "Müşteri Onayı Bekliyor",
        "Beklemede", "Teslime Hazır", "Tamamlandı", "İade Edildi", "Onarılamadı"
    ];
    private static readonly string[] AllowedControlStatuses = [
        "Kayıt Açıldı", "Serviste", "Onarımda", "Parça Bekliyor", "Müşteri Onayı Bekliyor",
        "Beklemede", "Teslime Hazır", "İade Edildi", "Onarılamadı"
    ];
    private static readonly string[] AllowedCurrencies = ["TRY", "USD", "EUR"];
    private static readonly string[] AllowedVatModes = ["KDV Dahil", "KDV Hariç"];
    private static readonly string[] AllowedDeliveryMethods = ["Müşteriye Teslim", "Kargo"];

    public ServiceController(ApplicationDbContext context, ServiceNumberGenerator numbers, AuditService audit, ILogger<ServiceController> logger, BusinessDayService businessDays, EmailService email)
    {
        _context = context;
        _numbers = numbers;
        _audit = audit;
        _logger = logger;
        _businessDays = businessDays;
        _email = email;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? status, string? warranty, string? currency, DateTime? dateFrom, DateTime? dateTo, bool showDeleted = false, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = pageSize is 20 or 50 or 100 ? pageSize : 20;
        var query = _context.ServiceRecords.AsNoTracking().AsQueryable();
        if (!showDeleted || !User.IsInRole("Admin")) query = query.Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var rawSearch = search.Trim();
            var upperSearch = UpperTr(rawSearch);
            var likeSearch = $"%{rawSearch}%";
            query = query.Where(x => x.Brand.Contains(upperSearch) || x.Model.Contains(upperSearch) || x.SerialNumber.Contains(upperSearch) ||
                                     x.Sender!.Contains(upperSearch) || x.SentToService.Contains(upperSearch) || x.TrackingNumber.Contains(rawSearch) ||
                                     EF.Functions.Like(x.CustomerEmail ?? string.Empty, likeSearch) || EF.Functions.Like(x.CustomerPhone ?? string.Empty, likeSearch));
            search = rawSearch;
        }
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (warranty == "yes") query = query.Where(x => x.IsUnderWarranty);
        if (warranty == "no") query = query.Where(x => !x.IsUnderWarranty);
        if (!string.IsNullOrWhiteSpace(currency) && AllowedCurrencies.Contains(currency)) query = query.Where(x => x.ChargeCurrency == currency);
        if (dateFrom.HasValue) query = query.Where(x => x.CreatedAt >= dateFrom.Value.Date);
        if (dateTo.HasValue) query = query.Where(x => x.CreatedAt < dateTo.Value.Date.AddDays(1));

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);
        var records = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        ViewBag.Search = search; ViewBag.Status = status; ViewBag.Warranty = warranty; ViewBag.Currency = currency;
        ViewBag.DateFrom = dateFrom?.ToString("yyyy-MM-dd"); ViewBag.DateTo = dateTo?.ToString("yyyy-MM-dd"); ViewBag.ShowDeleted = showDeleted;
        ViewBag.Statuses = AllowedStatuses; ViewBag.Page = page; ViewBag.PageSize = pageSize; ViewBag.Total = total; ViewBag.TotalPages = totalPages;
        return View(records);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult AddService(bool fresh = false)
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        ViewBag.Fresh = fresh;
        return View(new ServiceRecord { ServiceArrivalDate = DateTime.Today, ServiceSentDate = null, Status = "Kayıt Açıldı", ChargeCurrency = "TRY", ChargeVatMode = "KDV Hariç", DeliveryMethod = "Müşteriye Teslim", IsUnderWarranty = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddService(ServiceRecord serviceRecord)
    {
        SanitizeAndValidate(serviceRecord);
        if (!ModelState.IsValid) return View(serviceRecord);

        var (userId, username) = CurrentUser();
        NormalizeOptionalFields(serviceRecord);
        serviceRecord.CreatedAt = DateTime.Now;
        serviceRecord.ServiceArrivalDate = serviceRecord.CreatedAt.Date;
        serviceRecord.ServiceSentDate = null;
        serviceRecord.TrackingNumber = await _numbers.NextAsync(serviceRecord.ServiceArrivalDate);
        serviceRecord.CreatedByUserId = userId;
        serviceRecord.CreatedByUsername = username;
        serviceRecord.Status = "Kayıt Açıldı";
        serviceRecord.ServiceResult = string.Empty;
        serviceRecord.HasCharge = false;
        serviceRecord.ChargeAmount = null;
        serviceRecord.ChargeCurrency = "TRY";
        serviceRecord.ChargeVatMode = "KDV Hariç";
        serviceRecord.DeliveryMethod = "Müşteriye Teslim";
        serviceRecord.DeliveryDate = null;
        serviceRecord.IsRepaired = false;
        serviceRecord.IsDeleted = false;
        serviceRecord.DeletedAt = null;
        serviceRecord.DeletedByUserId = null;
        serviceRecord.DeletedByUsername = string.Empty;
        serviceRecord.UpdatedAt = null;
        serviceRecord.UpdatedByUserId = null;
        serviceRecord.UpdatedByUsername = string.Empty;
        foreach (var part in serviceRecord.Parts)
        {
            part.UnitPrice = null;
            part.Currency = "TRY";
            part.Notes = string.Empty;
        }

        _context.ServiceRecords.Add(serviceRecord);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _context.Entry(serviceRecord).State = EntityState.Detached;
            ModelState.AddModelError(string.Empty, "Kayıt veritabanına kaydedilemedi. Lütfen zorunlu alanları kontrol edip tekrar deneyin.");
            _logger.LogError(ex, "Servis kaydı veritabanına yazılamadı. Takip no: {TrackingNumber}", serviceRecord.TrackingNumber);
            await _audit.LogAsync("Servis Kayıt Hatası", "ServiceRecord", serviceRecord.TrackingNumber,
                "Servis kaydı veritabanına yazılamadı. Teknik detay sunucu loguna kaydedildi.");
            return View(serviceRecord);
        }

        await _audit.LogAsync("Servis Kaydı Oluşturuldu", "ServiceRecord", serviceRecord.Id,
            $"{serviceRecord.TrackingNumber} numaralı servis kaydı oluşturuldu. İlk kayıt bilgileri aşağıda yer almaktadır.", null, Snapshot(serviceRecord));

        if (!string.IsNullOrWhiteSpace(serviceRecord.CustomerEmail))
        {
            var intakeAttachment = new EmailAttachment(
                $"Servis-Kayit-Formu-{serviceRecord.TrackingNumber.Replace('/', '-')}.html",
                "text/html; charset=utf-8",
                MailTemplateService.IntakeAttachment(serviceRecord));
            var mailResult = await _email.SendAsync(
                serviceRecord.CustomerEmail,
                $"Ayaz Teknoloji - Servis Kaydı {serviceRecord.TrackingNumber}",
                MailTemplateService.IntakeMail(serviceRecord),
                true,
                [intakeAttachment]);
            await _audit.LogAsync(mailResult.Success ? "Müşteri Kayıt Maili Gönderildi" : "Müşteri Kayıt Maili Hatası",
                "ServiceRecord", serviceRecord.Id,
                mailResult.Success
                    ? $"{serviceRecord.TrackingNumber} servis kabul formu müşteriye e-posta ile gönderildi."
                    : $"{serviceRecord.TrackingNumber} servis kabul maili gönderilemedi: {mailResult.Message}");
            if (!mailResult.Success)
                TempData["WarningMessage"] = "Servis kaydı oluşturuldu ancak müşteriye kayıt e-postası gönderilemedi.";
        }

        TempData["SuccessMessage"] = $"{serviceRecord.TrackingNumber} numaralı servis kaydı oluşturuldu.";
        return RedirectToAction(nameof(ServiceReport), new { id = serviceRecord.Id, fromCreate = true });
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel()
    {
        var data = await _context.ServiceRecords.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        var rows = data.Select(x => (IReadOnlyList<object?>)new object?[]
        {
            x.TrackingNumber, x.CreatedAt, x.ServiceSentDate, x.Sender, x.CustomerEmail, x.CustomerPhone, x.Brand, x.Model, x.ProductType,
            x.SerialNumber, x.SentToService, x.FaultReason, x.Status, x.IsUnderWarranty,
            x.ServiceResult, x.HasCharge, x.ChargeAmount, x.ChargeCurrency, x.ChargeVatMode, x.DeliveryMethod,
            x.DeliveryDate, x.CreatedByUsername, x.CreatedAt, x.UpdatedByUsername, x.UpdatedAt
        }).ToList();

        var bytes = XlsxExportService.Create(
            "Servis Kayıtları",
            ["Takip No","Kayıt Tarihi","Servise Gidiş Tarihi","Gönderen","Müşteri E-postası","Müşteri Telefonu","Marka","Model","Cins","Seri No","Gönderilen Servis",
             "Arıza Nedeni","Durum","Garantili","Yapılan İşlem","Ücret Var","Tutar","Para Birimi","KDV Durumu",
             "Teslim Şekli","Teslim Tarihi","Kaydı Açan","Oluşturma Zamanı","Güncelleyen","Güncelleme Tarihi"],
            rows);

        await _audit.LogAsync("Excel Dışa Aktarma", "ServiceRecord", "All",
            "Servis kayıtları Excel olarak dışa aktarıldı.");
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ServisKayitlari-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportSingleExcel(int id)
    {
        var x = await _context.ServiceRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (x == null) return NotFound();
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { x.TrackingNumber, x.CreatedAt, x.ServiceSentDate, x.Sender, x.CustomerEmail, x.CustomerPhone, x.Brand, x.Model, x.ProductType,
                x.SerialNumber, x.SentToService, x.FaultReason, x.Status, x.IsUnderWarranty ? "Garantili" : "Garantisiz",
                x.ServiceResult, x.HasCharge ? "Evet" : "Hayır", x.ChargeAmount, x.ChargeCurrency, x.ChargeVatMode, x.DeliveryMethod,
                x.DeliveryDate, x.CreatedByUsername, x.CreatedAt, x.UpdatedByUsername, x.UpdatedAt }
        };
        var bytes = XlsxExportService.Create("Servis Kaydı",
            ["Takip No","Kayıt Tarihi","Servise Gidiş Tarihi","Gönderen","Müşteri E-postası","Müşteri Telefonu","Marka","Model","Cins","Seri No","Gönderilen Servis",
             "Arıza Nedeni","Durum","Garanti","Yapılan İşlem","Ücret Var","Tutar","Para Birimi","KDV Durumu",
             "Teslim Şekli","Teslim Tarihi","Kaydı Açan","Oluşturma Zamanı","Güncelleyen","Güncelleme Tarihi"], rows);
        await _audit.LogAsync("Excel Dışa Aktarma", "ServiceRecord", id, $"{x.TrackingNumber} numaralı kayıt Excel olarak dışa aktarıldı.");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Servis-{x.TrackingNumber.Replace('/', '-')}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> AllReport()
    {
        var data = await _context.ServiceRecords.AsNoTracking().Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt).ToListAsync();
        await _audit.LogAsync("PDF Raporu", "ServiceRecord", "All", "Tüm servis kayıtları yazdırılabilir PDF raporu olarak açıldı.");
        return View(data);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var record = await _context.ServiceRecords.Include(x => x.Parts).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (record == null || (record.IsDeleted && !User.IsInRole("Admin"))) return NotFound();
        ViewBag.Logs = await _context.AuditLogs.AsNoTracking()
            .Where(x => x.EntityType == "ServiceRecord" && x.EntityId == id.ToString())
            .OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync();
        ViewBag.LogTotal = await _context.AuditLogs.AsNoTracking().CountAsync(x => x.EntityType == "ServiceRecord" && x.EntityId == id.ToString());
        ViewBag.Statuses = AllowedStatuses;
        ViewBag.DeviceHistory = await _context.ServiceRecords.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id != record.Id && x.SerialNumber == record.SerialNumber)
            .OrderByDescending(x => x.CreatedAt).Take(5).ToListAsync();
        ViewBag.BusinessDays = _businessDays.Count(record);
        ViewBag.BusinessDayText = _businessDays.Describe(record);
        ViewBag.BusinessDayLevel = _businessDays.Level(record);
        return View(record);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var record = await _context.ServiceRecords.Include(x => x.Parts).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        return record == null ? NotFound() : View(record);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ServiceRecord form)
    {
        if (id != form.Id) return BadRequest();
        SanitizeAndValidate(form);
        var record = await _context.ServiceRecords.Include(x => x.Parts).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (record == null) return NotFound();
        if (!ModelState.IsValid)
        {
            form.TrackingNumber = record.TrackingNumber;
            form.CreatedAt = record.CreatedAt;
            return View(form);
        }

        var old = Snapshot(record);
        var (_, username) = CurrentUser();
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;

        record.Sender = UpperTr(form.Sender);
        record.CustomerEmail = form.CustomerEmail?.Trim() ?? string.Empty;
        record.CustomerPhone = form.CustomerPhone?.Trim() ?? string.Empty;
        record.Brand = UpperTr(form.Brand);
        record.Model = UpperTr(form.Model);
        record.ProductType = UpperTr(form.ProductType);
        record.SerialNumber = UpperTr(form.SerialNumber);
        record.SentToService = UpperTr(form.SentToService);
        record.FaultReason = UpperTr(form.FaultReason);
        record.Accessories = UpperTr(form.Accessories);
        record.Notes = UpperTr(form.Notes);
        record.IsUnderWarranty = form.IsUnderWarranty;
        record.UpdatedAt = DateTime.Now;
        record.UpdatedByUserId = userId;
        record.UpdatedByUsername = username;

        _context.ServiceParts.RemoveRange(record.Parts);
        record.Parts = form.Parts.Select(p => new ServicePart
        {
            PartType = p.PartType,
            PartName = UpperTr(p.PartName),
            SerialNumber = UpperTr(p.SerialNumber),
            Quantity = p.Quantity,
            UnitPrice = p.UnitPrice,
            Currency = p.Currency,
            Notes = UpperTr(p.Notes)
        }).ToList();

        await _context.SaveChangesAsync();
        await _audit.LogAsync("Servis Kaydı Güncellendi", "ServiceRecord", record.Id,
            $"{record.TrackingNumber} numaralı servis kaydı güncellendi. Değiştirilen alanlar aşağıda gösterilmektedir.", old, Snapshot(record));
        TempData["SuccessMessage"] = "Servis kaydı ve parça hareketleri güncellendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Control(int id)
    {
        var record = await _context.ServiceRecords.Include(x => x.Parts).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (record == null) return NotFound();
        if (record.Status == "Tamamlandı" && record.DeliveryDate.HasValue)
        {
            TempData["WarningMessage"] = "Teslimi tamamlanmış kayıtların teknik kontrolü yeniden açılamaz. Ürün kimlik bilgilerini Kayıt Bilgileri ekranından düzenleyebilirsiniz.";
            return RedirectToAction(nameof(Details), new { id });
        }
        ViewBag.Statuses = AllowedControlStatuses;
        return View(record);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Control(int id, string status, decimal? chargeAmount, string chargeCurrency, string chargeVatMode, bool isUnderWarranty, string? serviceResult, string? notes)
    {
        if (!AllowedCurrencies.Contains(chargeCurrency) || !AllowedVatModes.Contains(chargeVatMode) || !AllowedControlStatuses.Contains(status)) return BadRequest();
        var resultRequired = status is "Teslime Hazır" or "Tamamlandı" or "İade Edildi" or "Onarılamadı";
        if (resultRequired && string.IsNullOrWhiteSpace(serviceResult))
        {
            TempData["ErrorMessage"] = "Bu durum için yapılan işlem/sonuç alanını doldurmalısınız.";
            return RedirectToAction(nameof(Control), new { id });
        }
        if (chargeAmount.HasValue && chargeAmount.Value < 0)
        {
            TempData["ErrorMessage"] = "Servis ücreti negatif olamaz.";
            return RedirectToAction(nameof(Control), new { id });
        }
        if ((serviceResult?.Length ?? 0) > 6000 || (notes?.Length ?? 0) > 4000)
        {
            TempData["ErrorMessage"] = "Yapılan işlem en fazla 6.000, kontrol notu en fazla 4.000 karakter olabilir.";
            return RedirectToAction(nameof(Control), new { id });
        }

        var record = await _context.ServiceRecords.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (record == null) return NotFound();
        if (record.Status == "Tamamlandı" && record.DeliveryDate.HasValue)
        {
            TempData["WarningMessage"] = "Teslimi tamamlanmış kayıt yeniden kontrol edilemez.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var oldStatus = record.Status;
        var old = Snapshot(record);
        if (_businessDays.IsPreService(oldStatus) && !_businessDays.IsPreService(status) && !record.ServiceSentDate.HasValue)
            record.ServiceSentDate = DateTime.Today;
        var (userId, username) = CurrentUser();
        record.ServiceResult = UpperTr(serviceResult);
        record.ControlNotes = UpperTr(notes);
        record.IsUnderWarranty = isUnderWarranty;
        record.HasCharge = chargeAmount.HasValue && chargeAmount.Value > 0;
        record.ChargeAmount = record.HasCharge ? chargeAmount : null;
        record.ChargeCurrency = chargeCurrency;
        record.ChargeVatMode = record.HasCharge ? chargeVatMode : "KDV Hariç";
        record.Status = status;
        record.UpdatedAt = DateTime.Now;
        record.UpdatedByUserId = userId;
        record.UpdatedByUsername = username;
        await _context.SaveChangesAsync();
        await _audit.LogAsync("Teknik Kontrol Kaydedildi", "ServiceRecord", record.Id,
            $"{record.TrackingNumber} numaralı ürünün teknik kontrol bilgileri kaydedildi. Değiştirilen alanlar aşağıda gösterilmektedir.", old, Snapshot(record));

        if (!string.Equals(oldStatus, record.Status, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(record.CustomerEmail) &&
            MailTemplateService.StatusMessage(record) != null)
        {
            var mailResult = await SendCustomerStatusEmailAsync(record);
            await _audit.LogAsync(mailResult.Success ? "Müşteri Durum Maili Gönderildi" : "Müşteri Durum Maili Hatası",
                "ServiceRecord", record.Id,
                mailResult.Success
                    ? $"{record.TrackingNumber} için '{record.Status}' durum bildirimi müşteriye gönderildi."
                    : $"{record.TrackingNumber} için müşteri e-postası gönderilemedi: {mailResult.Message}");
            if (!mailResult.Success) TempData["WarningMessage"] = "Kayıt güncellendi ancak müşteriye durum e-postası gönderilemedi.";
        }

        TempData["SuccessMessage"] = $"Teknik kontrol kaydedildi. Güncel durum: {record.Status}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> DeliveryQueue(string? search, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = pageSize is 20 or 50 or 100 ? pageSize : 20;
        var q = _context.ServiceRecords.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == "Teslime Hazır");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var rawSearch = search.Trim();
            var upperSearch = UpperTr(rawSearch);
            var likeSearch = $"%{rawSearch}%";
            q = q.Where(x => x.TrackingNumber.Contains(rawSearch) || x.Brand.Contains(upperSearch) || x.Model.Contains(upperSearch) || x.SerialNumber.Contains(upperSearch) ||
                          x.Sender!.Contains(upperSearch) || EF.Functions.Like(x.CustomerEmail ?? string.Empty, likeSearch) || EF.Functions.Like(x.CustomerPhone ?? string.Empty, likeSearch));
            search = rawSearch;
        }
        var total = await q.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);
        ViewBag.Search = search; ViewBag.Page = page; ViewBag.PageSize = pageSize; ViewBag.Total = total; ViewBag.TotalPages = totalPages;
        return View(await q.OrderBy(x => x.UpdatedAt ?? x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Delivery(int id)
    {
        var record = await _context.ServiceRecords.Include(x => x.Parts).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (record == null) return NotFound();
        if (record.Status != "Teslime Hazır")
        {
            TempData["WarningMessage"] = "Bu ürün henüz teslime hazır değil. Önce teknik kontrol durumunu ‘Teslime Hazır’ olarak kaydedin.";
            return RedirectToAction(nameof(Control), new { id });
        }
        return View(record);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delivery(int id, string deliveryMethod, DateTime deliveryDate)
    {
        if (!AllowedDeliveryMethods.Contains(deliveryMethod)) return BadRequest();
        if (deliveryDate == default || deliveryDate.Date > DateTime.Today)
        {
            TempData["ErrorMessage"] = "Teslim tarihi boş veya ileri bir tarih olamaz.";
            return RedirectToAction(nameof(Delivery), new { id });
        }
        var record = await _context.ServiceRecords.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (record == null) return NotFound();
        if (deliveryDate.Date < record.CreatedAt.Date)
        {
            TempData["ErrorMessage"] = "Teslim tarihi kayıt tarihinden önce olamaz.";
            return RedirectToAction(nameof(Delivery), new { id });
        }
        if (record.Status != "Teslime Hazır" || string.IsNullOrWhiteSpace(record.ServiceResult))
        {
            TempData["ErrorMessage"] = "Teslimden önce kontrol ekranından cihaz durumunu ‘Teslime Hazır’ yapmalı ve yapılan işlemi kaydetmelisiniz.";
            return RedirectToAction(nameof(Control), new { id });
        }

        var old = Snapshot(record);
        var (userId, username) = CurrentUser();
        record.DeliveryMethod = deliveryMethod;
        record.DeliveryDate = deliveryDate.Date;
        record.Status = "Tamamlandı";
        record.IsRepaired = true;
        record.UpdatedAt = DateTime.Now;
        record.UpdatedByUserId = userId;
        record.UpdatedByUsername = username;
        await _context.SaveChangesAsync();
        await _audit.LogAsync("Ürün Teslim Edildi", "ServiceRecord", record.Id,
            $"{record.TrackingNumber} numaralı ürün {deliveryMethod} yöntemiyle teslim edildi. Teslim sırasında değişen kayıt bilgileri aşağıda gösterilmektedir.", old, Snapshot(record));

        if (!string.IsNullOrWhiteSpace(record.CustomerEmail))
        {
            var deliveryMail = await SendCustomerStatusEmailAsync(record);
            await _audit.LogAsync(deliveryMail.Success ? "Teslim Maili Gönderildi" : "Teslim Maili Hatası",
                "ServiceRecord", record.Id,
                deliveryMail.Success ? $"{record.TrackingNumber} teslim bilgisi müşteriye gönderildi." : $"{record.TrackingNumber} teslim maili gönderilemedi: {deliveryMail.Message}");
        }

        TempData["SuccessMessage"] = "Ürün teslimi tamamlandı.";
        return RedirectToAction(nameof(DeliveryReport), new { id });
    }


    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendCustomerNotification(int id)
    {
        var record = await _context.ServiceRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (record == null) return NotFound();
        if (MailTemplateService.StatusMessage(record) == null && record.Status != "Kayıt Açıldı")
        {
            TempData["WarningMessage"] = "Bu durum için hazır müşteri e-posta şablonu bulunmuyor.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (string.IsNullOrWhiteSpace(record.CustomerEmail))
        {
            TempData["WarningMessage"] = "Bu kayıtta müşteri e-posta adresi bulunmuyor.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = record.Status == "Kayıt Açıldı"
            ? await _email.SendAsync(record.CustomerEmail!, $"Ayaz Teknoloji - Servis Kaydı {record.TrackingNumber}", MailTemplateService.IntakeMail(record))
            : await SendCustomerStatusEmailAsync(record);
        await _audit.LogAsync(result.Success ? "Müşteri Bildirimi Gönderildi" : "Müşteri Bildirimi Hatası",
            "ServiceRecord", record.Id,
            result.Success ? $"{record.TrackingNumber} için müşteri bildirimi yeniden gönderildi." : $"{record.TrackingNumber} için müşteri bildirimi gönderilemedi.");
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var record = await _context.ServiceRecords.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (record == null) return NotFound();
        var (userId, username) = CurrentUser();
        record.IsDeleted = true;
        record.DeletedAt = DateTime.Now;
        record.DeletedByUserId = userId;
        record.DeletedByUsername = username;
        record.UpdatedAt = DateTime.Now;
        await _context.SaveChangesAsync();
        await _audit.LogAsync("Servis Kaydı Silindi", "ServiceRecord", record.Id, $"{record.TrackingNumber} numaralı kayıt çöp kutusuna taşındı.", Snapshot(record), null);
        TempData["SuccessMessage"] = "Kayıt silindi. Yönetici çöp kutusundan geri yükleyebilir.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id)
    {
        var record = await _context.ServiceRecords.FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
        if (record == null) return NotFound();
        record.IsDeleted = false; record.DeletedAt = null; record.DeletedByUserId = null; record.DeletedByUsername = string.Empty;
        record.UpdatedAt = DateTime.Now;
        var (userId, username) = CurrentUser(); record.UpdatedByUserId = userId; record.UpdatedByUsername = username;
        await _context.SaveChangesAsync();
        await _audit.LogAsync("Servis Kaydı Geri Yüklendi", "ServiceRecord", record.Id, $"{record.TrackingNumber} numaralı kayıt geri yüklendi.");
        TempData["SuccessMessage"] = "Kayıt geri yüklendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = "Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PermanentDelete(int id)
    {
        var record = await _context.ServiceRecords.Include(x => x.Parts).FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
        if (record == null) return NotFound();

        var trackingNumber = record.TrackingNumber;
        var snapshot = Snapshot(record);
        _context.ServiceRecords.Remove(record);
        await _context.SaveChangesAsync();
        await _audit.LogAsync("Servis Kaydı Kalıcı Silindi", "ServiceRecord", id,
            $"{trackingNumber} numaralı kayıt çöp kutusundan kalıcı olarak silindi.", snapshot, null);

        TempData["SuccessMessage"] = $"{trackingNumber} numaralı kayıt kalıcı olarak silindi.";
        return RedirectToAction(nameof(Index), new { showDeleted = true });
    }

    [HttpGet]
    public async Task<IActionResult> DeviceHistory(string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return BadRequest();
        serialNumber = serialNumber.Trim();
        ViewBag.SerialNumber = serialNumber;
        var items = await _context.ServiceRecords.AsNoTracking().Where(x => !x.IsDeleted && EF.Functions.Collate(x.SerialNumber, "NOCASE") == serialNumber)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToListAsync();
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> ServiceReport(int id, bool autoPrint = false, bool fromCreate = false)
    {
        var record = await _context.ServiceRecords.Include(x => x.Parts).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (record == null) return NotFound();
        ViewBag.AutoPrint = autoPrint;
        ViewBag.FromCreate = fromCreate;
        return View(record);
    }

    [HttpGet]
    public async Task<IActionResult> DeliveryReport(int id)
    {
        var record = await _context.ServiceRecords.Include(x => x.Parts).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (record == null) return NotFound();
        if (record.Status != "Tamamlandı" || !record.DeliveryDate.HasValue)
        {
            TempData["WarningMessage"] = "Teslim raporu yalnızca teslim tamamlandıktan sonra oluşturulabilir.";
            return RedirectToAction(nameof(Details), new { id });
        }
        return View(record);
    }


    private Task<EmailSendResult> SendCustomerStatusEmailAsync(ServiceRecord record)
    {
        var template = MailTemplateService.StatusMessage(record);
        if (template == null)
            return Task.FromResult(new EmailSendResult(false, "Bu durum için e-posta şablonu bulunmuyor."));

        var body = MailTemplateService.ServiceStatus(record, template.Value.Title, template.Value.Intro, template.Value.Accent, template.Value.Action);
        return _email.SendAsync(record.CustomerEmail!, $"Ayaz Teknoloji - {record.TrackingNumber} - {template.Value.Title}", body);
    }

    private void SanitizeAndValidate(ServiceRecord record)
    {
        record.Parts ??= new List<ServicePart>();
        ModelState.Remove(nameof(ServiceRecord.Sender));
        ModelState.Remove(nameof(ServiceRecord.Notes));
        ModelState.Remove(nameof(ServiceRecord.Accessories));
        ModelState.Remove(nameof(ServiceRecord.ControlNotes));
        ModelState.Remove(nameof(ServiceRecord.ServiceResult));
        foreach (var key in ModelState.Keys.Where(k => k.StartsWith("Parts[", StringComparison.OrdinalIgnoreCase)).ToList()) ModelState.Remove(key);
        record.Sender = UpperTr(record.Sender);
        record.Brand = UpperTr(record.Brand);
        record.Model = UpperTr(record.Model);
        record.ProductType = UpperTr(record.ProductType);
        record.SerialNumber = UpperTr(record.SerialNumber);
        record.FaultReason = UpperTr(record.FaultReason);
        record.CustomerEmail = record.CustomerEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        record.CustomerPhone = record.CustomerPhone?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(record.CustomerEmail) &&
            !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(record.CustomerEmail))
            ModelState.AddModelError(nameof(record.CustomerEmail), "Geçerli bir müşteri e-posta adresi girin veya alanı boş bırakın.");

        if (!string.IsNullOrWhiteSpace(record.CustomerPhone))
        {
            var compactPhone = new string(record.CustomerPhone.Where(c => char.IsDigit(c) || c == '+').ToArray());
            var digitCount = compactPhone.Count(char.IsDigit);
            if ((compactPhone.StartsWith('+') && compactPhone.Count(c => c == '+') > 1) ||
                (!compactPhone.StartsWith('+') && compactPhone.Contains('+')) || digitCount < 10 || digitCount > 15)
                ModelState.AddModelError(nameof(record.CustomerPhone), "Telefon numarası 10-15 rakam içermeli; yalnızca rakam, boşluk, +, parantez, nokta ve tire kullanılabilir.");
        }

        record.SentToService = UpperTr(record.SentToService);
        record.Accessories = UpperTr(record.Accessories);
        record.Notes = UpperTr(record.Notes);
        record.ControlNotes = UpperTr(record.ControlNotes);
        record.ServiceResult = UpperTr(record.ServiceResult);
        if (string.IsNullOrWhiteSpace(record.SentToService))
            ModelState.AddModelError(nameof(record.SentToService), "Gönderilen servis zorunludur.");

        record.Parts = record.Parts.Where(p => !string.IsNullOrWhiteSpace(p.PartName) || !string.IsNullOrWhiteSpace(p.SerialNumber)).ToList();
        foreach (var part in record.Parts)
        {
            if (part.PartType is not ("Takılan" or "Çıkarılan")) ModelState.AddModelError("Parts", "Geçersiz parça işlem türü.");
            if (string.IsNullOrWhiteSpace(part.PartName)) ModelState.AddModelError("Parts", "Eklenen her parça için parça adı zorunludur.");
            if (!AllowedCurrencies.Contains(part.Currency)) part.Currency = "TRY";
            if (part.Quantity < 1) part.Quantity = 1;
        }
        if (!AllowedStatuses.Contains(record.Status)) ModelState.AddModelError(nameof(record.Status), "Geçersiz servis durumu.");
        if (!AllowedCurrencies.Contains(record.ChargeCurrency)) record.ChargeCurrency = "TRY";
        if (!AllowedVatModes.Contains(record.ChargeVatMode)) record.ChargeVatMode = "KDV Hariç";
        if (!AllowedDeliveryMethods.Contains(record.DeliveryMethod)) record.DeliveryMethod = "Müşteriye Teslim";
        if (record.HasCharge && (!record.ChargeAmount.HasValue || record.ChargeAmount < 0)) ModelState.AddModelError(nameof(record.ChargeAmount), "Ücret çıktıysa geçerli tutar girin.");
    }

    private static void NormalizeOptionalFields(ServiceRecord record)
    {
        record.Sender = UpperTr(record.Sender);
        record.Brand = UpperTr(record.Brand);
        record.Model = UpperTr(record.Model);
        record.ProductType = UpperTr(record.ProductType);
        record.SerialNumber = UpperTr(record.SerialNumber);
        record.FaultReason = UpperTr(record.FaultReason);
        record.CustomerEmail = record.CustomerEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        record.CustomerPhone = record.CustomerPhone?.Trim() ?? string.Empty;
        record.Accessories = UpperTr(record.Accessories);
        record.Notes = UpperTr(record.Notes);
        record.ControlNotes = UpperTr(record.ControlNotes);
        record.ServiceResult = UpperTr(record.ServiceResult);
        record.SentToService = UpperTr(record.SentToService);
        record.Parts ??= new List<ServicePart>();
        foreach (var part in record.Parts)
        {
            part.PartName = UpperTr(part.PartName);
            part.SerialNumber = UpperTr(part.SerialNumber);
            part.Notes = UpperTr(part.Notes);
            part.Currency = string.IsNullOrWhiteSpace(part.Currency) ? "TRY" : part.Currency;
        }
    }

    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");
    private static string UpperTr(string? value) => (value ?? string.Empty).Trim().ToUpper(TurkishCulture);

    private (int? UserId, string Username) CurrentUser()
    {
        int? id = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var value) ? value : null;
        return (id, User.Identity?.Name ?? "Bilinmiyor");
    }

    private static object Snapshot(ServiceRecord r) => new
    {
        r.TrackingNumber, r.Sender, r.CustomerEmail, r.CustomerPhone, r.Brand, r.Model, r.ProductType, r.SerialNumber, r.ServiceArrivalDate, r.ServiceSentDate, r.SentToService,
        r.FaultReason, r.Accessories, r.Notes, r.ControlNotes, r.Status, r.ServiceResult, r.IsUnderWarranty, r.HasCharge, r.ChargeAmount,
        r.ChargeCurrency, r.ChargeVatMode, r.DeliveryMethod, r.DeliveryDate, Parts = r.Parts.Select(p => new { p.PartType, p.PartName, p.SerialNumber, p.Quantity, p.UnitPrice, p.Currency }).ToArray()
    };
}
