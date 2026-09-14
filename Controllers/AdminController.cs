using System.Globalization;
using System.Security.Claims;
using AyazTekServis.Models;
using AyazTekServis.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AyazTekServis.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly AuditService _audit;
    private readonly EmailService _email;
    private readonly TokenService _tokens;
    private readonly SecretProtectionService _secretProtection;
    private readonly DatabaseBackupService _backupService;

    public AdminController(ApplicationDbContext db, IConfiguration configuration, AuditService audit, EmailService email, TokenService tokens, SecretProtectionService secretProtection, DatabaseBackupService backupService)
    {
        _db = db; _configuration = configuration; _audit = audit; _email = email; _tokens = tokens; _secretProtection = secretProtection; _backupService = backupService;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.UserCount = await _db.Users.CountAsync();
        ViewBag.PendingUsers = await _db.Users.CountAsync(x => !x.IsActive);
        ViewBag.LogCount = await _db.AuditLogs.CountAsync();
        ViewBag.DeletedServiceCount = await _db.ServiceRecords.CountAsync(x => x.IsDeleted);
        ViewBag.RecentLogs = await _db.AuditLogs.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(12).ToListAsync();
        ViewBag.ActiveSessionCount = await _db.UserSessions.Where(x => x.RevokedAt == null && x.ExpiresAt > DateTime.Now).Select(x => x.UserId).Distinct().CountAsync();
        ViewBag.ErrorCount24h = await _db.ErrorLogs.CountAsync(x => x.CreatedAt >= DateTime.Now.AddDays(-1));
        var lastBackup = await _db.BackupHistories.AsNoTracking().Where(x => x.IsSuccessful).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
        ViewBag.LastBackup = lastBackup;
        ViewBag.BackupOverdue = lastBackup == null || lastBackup.CreatedAt < DateTime.Now.AddDays(-7);
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Users(string? search, string? state, string? role, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = pageSize is 20 or 50 or 100 ? pageSize : 20;
        var q = _db.Users.AsNoTracking().AsQueryable();
        if (state == "active") q = q.Where(x => x.IsActive);
        else if (state == "passive") q = q.Where(x => !x.IsActive);
        if (role == "admin") q = q.Where(x => x.IsAdmin);
        else if (role == "user") q = q.Where(x => !x.IsAdmin);
        var allUsers = await q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToListAsync();
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            allUsers = allUsers.Where(x => ContainsIgnoreCaseTr(x.Username, search) || ContainsIgnoreCaseTr(x.Email, search)).ToList();
        }

        var total = allUsers.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);
        var users = allUsers.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        ViewBag.Search = search; ViewBag.State = state; ViewBag.Role = role;
        ViewBag.Page = page; ViewBag.PageSize = pageSize; ViewBag.Total = total; ViewBag.TotalPages = totalPages;
        return View(users);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateUser(int id, bool isActive, bool isAdmin)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();
        var currentId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var cid) ? cid : 0;
        if (id == currentId && (!isActive || !isAdmin))
        {
            TempData["ErrorMessage"] = "Kendi yönetici yetkinizi veya aktif durumunuzu kaldıramazsınız.";
            return RedirectToAction(nameof(Users));
        }

        var old = new { user.IsActive, user.IsAdmin };
        var activeChanged = user.IsActive != isActive;
        user.IsActive = isActive; user.IsAdmin = isAdmin;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Kullanıcı Güncellendi", "User", user.Id, $"{user.Username} kullanıcısının yetkileri güncellendi.", old, new { user.IsActive, user.IsAdmin });

        if (activeChanged)
        {
            await _email.SendAsync(user.Email, "Ayaz Teknoloji - Hesap Durumu",
                MailTemplateService.Account(
                    user.IsActive ? "Hesabınız aktif" : "Hesabınız pasif",
                    $"Merhaba {user.Username}, hesabınızın durumu yönetici tarafından güncellendi.",
                    $"""<div style="padding:14px 16px;border-radius:12px;background:#f8fafc;border:1px solid #e3e8ef">Güncel durum: <strong>{(user.IsActive ? "Aktif" : "Pasif")}</strong></div>""",
                    user.IsActive ? "Aktif" : "Pasif"));
        }

        TempData["SuccessMessage"] = "Kullanıcı güncellendi.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var currentId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var cid) ? cid : 0;
        if (id == currentId)
        {
            TempData["ErrorMessage"] = "Kendi hesabınızı silemezsiniz.";
            return RedirectToAction(nameof(Users));
        }
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();
        await _audit.LogAsync("Kullanıcı Silindi", "User", user.Id, $"{user.Username} kullanıcısı silindi.");
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Kullanıcı silindi.";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> Logs(string? search, string? actionFilter, string? entityType, string? username, DateTime? dateFrom, DateTime? dateTo, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = pageSize is 20 or 50 or 100 ? pageSize : 20;
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType)) q = q.Where(x => x.EntityType == entityType.Trim());
        if (dateFrom.HasValue) q = q.Where(x => x.CreatedAt >= dateFrom.Value.Date);
        if (dateTo.HasValue) q = q.Where(x => x.CreatedAt < dateTo.Value.Date.AddDays(1));
        var allLogs = await q.OrderByDescending(x => x.CreatedAt).ToListAsync();
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            allLogs = allLogs.Where(x => ContainsIgnoreCaseTr(x.Description, search) || ContainsIgnoreCaseTr(x.EntityId, search) || ContainsIgnoreCaseTr(x.IpAddress, search) || ContainsIgnoreCaseTr(x.Action, search) || ContainsIgnoreCaseTr(x.Username, search)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(actionFilter))
        {
            var value = actionFilter.Trim();
            allLogs = allLogs.Where(x => ContainsIgnoreCaseTr(x.Action, value)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(username))
        {
            var value = username.Trim();
            allLogs = allLogs.Where(x => ContainsIgnoreCaseTr(x.Username, value)).ToList();
        }
        var total = allLogs.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);
        var logs = allLogs.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        ViewBag.Search = search; ViewBag.ActionFilter = actionFilter; ViewBag.EntityType = entityType; ViewBag.Username = username;
        ViewBag.DateFrom = dateFrom?.ToString("yyyy-MM-dd"); ViewBag.DateTo = dateTo?.ToString("yyyy-MM-dd");
        ViewBag.Page = page; ViewBag.PageSize = pageSize; ViewBag.Total = total; ViewBag.TotalPages = totalPages;
        return View(logs);
    }

    [HttpGet]
    public async Task<IActionResult> SendEmail(int? userId)
    {
        ViewBag.Users = await _db.Users.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Username).ToListAsync();
        ViewBag.SelectedUserId = userId;
        ViewBag.SmtpConfigured = _email.IsConfigured;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendEmail(string recipientMode, int? userId, string? recipient, string subject, string body)
    {
        string target;
        User? selected = null;
        var manualRecipient = (recipient ?? string.Empty).Trim();

        var useRegisteredUser = string.Equals(recipientMode, "user", StringComparison.OrdinalIgnoreCase);
        if (useRegisteredUser)
        {
            if (!userId.HasValue)
            {
                TempData["ErrorMessage"] = "Bir kayıtlı kullanıcı seçin.";
                return RedirectToAction(nameof(SendEmail));
            }
            selected = await _db.Users.FindAsync(userId.Value);
            if (selected == null) return NotFound();
            target = selected.Email;
        }
        else
        {
            if (userId.HasValue || string.IsNullOrWhiteSpace(manualRecipient))
            {
                TempData["ErrorMessage"] = "Manuel e-posta adresini girin.";
                return RedirectToAction(nameof(SendEmail));
            }
            target = manualRecipient;
        }

        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(target) ||
            string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body) ||
            subject.Length > 200 || body.Length > 10000)
        {
            TempData["ErrorMessage"] = "Tek bir geçerli alıcı seçin; konu en fazla 200, mesaj en fazla 10.000 karakter olmalıdır.";
            return RedirectToAction(nameof(SendEmail), new { userId });
        }

        var safeBody = System.Net.WebUtility.HtmlEncode(body).Replace("\n", "<br>", StringComparison.Ordinal);
        var result = await _email.SendAsync(
            target,
            subject.Trim(),
            MailTemplateService.Account(
                subject.Trim(),
                "Ayaz Teknoloji tarafından size bir mesaj gönderildi.",
                $"""<div style="font-size:14px;line-height:1.7;color:#334155">{safeBody}</div>""",
                "Bilgilendirme"));

        await _audit.LogAsync(
            result.Success ? "E-posta Gönderildi" : "E-posta Hatası",
            "Email",
            selected?.Id.ToString() ?? target,
            $"{target} adresine '{subject}' konulu e-posta: {result.Message}"
        );

        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.Message;

        return RedirectToAction(nameof(SendEmail), new { userId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendPasswordReset(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();
        var previousTokens = await _db.PasswordResetTokens.Where(x => x.UserId == user.Id && x.UsedAt == null).ToListAsync();
        foreach (var previous in previousTokens) previous.UsedAt = DateTime.Now;
        var raw = _tokens.CreateToken();
        _db.PasswordResetTokens.Add(new PasswordResetToken { UserId = user.Id, TokenHash = _tokens.HashToken(raw), ExpiresAt = DateTime.Now.AddMinutes(30), CreatedAt = DateTime.Now });
        await _db.SaveChangesAsync();
        var url = await BuildPublicResetUrlAsync(user.Email, raw);
        if (string.IsNullOrWhiteSpace(url))
        {
            TempData["WarningMessage"] = "Şifre sıfırlama e-postası gönderilemedi. Sistem Ayarları bölümünde geçerli bir dış erişim adresi tanımlayın.";
            await _audit.LogAsync("Admin Şifre Reset Linki Oluşturulamadı", "User", user.Id, "Geçerli dış erişim adresi bulunamadığı için yönetici reset e-postası gönderilmedi.");
            return RedirectToAction(nameof(Users));
        }
        var safeUrl = System.Net.WebUtility.HtmlEncode(url);
        var result = await _email.SendAsync(user.Email, "Ayaz Teknoloji - Şifre Sıfırlama",
            MailTemplateService.Account(
                "Şifre sıfırlama bağlantısı",
                "Yönetici tarafından hesabınız için şifre sıfırlama bağlantısı oluşturuldu.",
                $"<div style=\"text-align:center;margin:18px 0\"><a href=\"{safeUrl}\" style=\"display:inline-block;background:#185c72;color:#fff;text-decoration:none;padding:12px 20px;border-radius:10px;font-weight:700\">Şifremi Sıfırla</a></div><p style=\"font-size:13px;color:#64748b\">Bağlantı 30 dakika geçerlidir.</p>",
                "Güvenlik"));
        await _audit.LogAsync("Şifre Sıfırlama Maili", "User", user.Id, $"{user.Username} için reset e-postası oluşturuldu: {result.Message}");
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Users));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TestEmail()
    {
        var adminId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        var admin = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == adminId);
        if (admin == null || string.IsNullOrWhiteSpace(admin.Email))
        {
            TempData["ErrorMessage"] = "Test e-postası için yönetici hesabında geçerli bir e-posta adresi bulunmalı.";
            return RedirectToAction(nameof(Settings));
        }
        var result = await _email.SendAsync(admin.Email, "Ayaz Teknoloji - SMTP Testi",
            MailTemplateService.Account(
                "E-posta sistemi çalışıyor",
                "SMTP bağlantısı başarıyla doğrulandı.",
                """<div style="padding:14px 16px;border-radius:12px;background:#ecfdf5;border:1px solid #bbf7d0;color:#166534"><strong>Başarılı:</strong> Ayaz Teknoloji servis sistemi e-posta gönderebiliyor.</div>""",
                "SMTP Testi"));
        await _audit.LogAsync(result.Success ? "SMTP Testi Başarılı" : "SMTP Testi Hatası", "Email", admin.Id, result.Message);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.Success
            ? $"Test e-postası {admin.Email} adresine gönderildi."
            : result.Message;
        return RedirectToAction(nameof(Settings));
    }

    [HttpGet]
    public async Task<IActionResult> Settings()
    {
        var settings = await _db.SystemSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value);
        ViewBag.CompanyName = settings.GetValueOrDefault("CompanyName", "AyazTek Servis");
        ViewBag.SupportEmail = settings.GetValueOrDefault("SupportEmail", "selcuksahin158@gmail.com");
        ViewBag.DefaultTheme = settings.GetValueOrDefault("DefaultTheme", "system");
        ViewBag.SmtpConfigured = _email.IsConfigured;
        ViewBag.SmtpHost = settings.GetValueOrDefault("SmtpHost", _configuration["Smtp:Host"] ?? "");
        ViewBag.SmtpPort = settings.GetValueOrDefault("SmtpPort", _configuration["Smtp:Port"] ?? "587");
        ViewBag.SmtpUsername = settings.GetValueOrDefault("SmtpUsername", _configuration["Smtp:Username"] ?? "");
        ViewBag.SmtpFrom = settings.GetValueOrDefault("SmtpFrom", _configuration["Smtp:From"] ?? "");
        ViewBag.SmtpEnableSsl = settings.GetValueOrDefault("SmtpEnableSsl", _configuration["Smtp:EnableSsl"] ?? "true");
        ViewBag.SmtpPasswordConfigured = settings.ContainsKey("SmtpPasswordProtected") || !string.IsNullOrWhiteSpace(_configuration["Smtp:Password"]);
        ViewBag.WeeklyReportRecipients = settings.GetValueOrDefault("WeeklyReportRecipients", _configuration["WeeklyReports:Recipients"] ?? "teknik@ayazteknoloji.com.tr");
        ViewBag.PublicBaseUrl = settings.GetValueOrDefault("PublicBaseUrl", _configuration["PublicBaseUrl"] ?? "");
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(string companyName, string supportEmail, string defaultTheme, string smtpHost, string smtpPort, string smtpUsername, string smtpFrom, bool smtpEnableSsl, string? smtpPassword, string? weeklyReportRecipients, string? publicBaseUrl)
    {
        if (!new[] { "system", "light", "dark" }.Contains(defaultTheme)) defaultTheme = "system";
        await UpsertSetting("CompanyName", (companyName ?? "AyazTek Servis").Trim());
        await UpsertSetting("SupportEmail", (supportEmail ?? string.Empty).Trim());
        await UpsertSetting("DefaultTheme", defaultTheme);
        var normalizedPublicUrl = (publicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        Uri? parsedPublicUrl = null;
        if (!string.IsNullOrWhiteSpace(normalizedPublicUrl) && !Uri.TryCreate(normalizedPublicUrl, UriKind.Absolute, out parsedPublicUrl))
        {
            TempData["ErrorMessage"] = "Dış erişim adresi geçerli bir HTTPS adresi olmalıdır.";
            return RedirectToAction(nameof(Settings));
        }
        if (parsedPublicUrl != null && !string.Equals(parsedPublicUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "Dış erişim adresi HTTPS kullanmalıdır.";
            return RedirectToAction(nameof(Settings));
        }
        await UpsertSetting("PublicBaseUrl", normalizedPublicUrl);
        await UpsertSetting("SmtpHost", (smtpHost ?? string.Empty).Trim());
        await UpsertSetting("SmtpPort", int.TryParse(smtpPort, out var port) && port > 0 && port <= 65535 ? port.ToString() : "587");
        await UpsertSetting("SmtpUsername", (smtpUsername ?? string.Empty).Trim());
        await UpsertSetting("SmtpFrom", (smtpFrom ?? string.Empty).Trim());
        await UpsertSetting("SmtpEnableSsl", smtpEnableSsl ? "true" : "false");
        if (!string.IsNullOrWhiteSpace(smtpPassword))
            await UpsertSetting("SmtpPasswordProtected", _secretProtection.Protect(smtpPassword));

        var validRecipients = (weeklyReportRecipients ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(x))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        await UpsertSetting("WeeklyReportRecipients", string.Join(",", validRecipients));
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Sistem Ayarları", "System", "Settings", "Sistem görünüm/kurum ayarları güncellendi.");
        TempData["SuccessMessage"] = "Sistem ayarları kaydedildi.";
        return RedirectToAction(nameof(Settings));
    }


    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Backup()
    {
        int? uid = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : null;
        try
        {
            var result = await _backupService.CreateAsync(HttpContext.RequestAborted);
            _db.BackupHistories.Add(new BackupHistory
            {
                CreatedAt = DateTime.Now,
                UserId = uid,
                Username = User.Identity?.Name ?? "Admin",
                FileName = result.FileName,
                SizeBytes = result.Bytes.LongLength,
                IsSuccessful = true
            });
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Veritabanı Yedeği", "System", "Database",
                $"Veritabanı yedeği oluşturuldu: {result.FileName} ({result.Bytes.LongLength:N0} bayt).");
            return File(result.Bytes, "application/x-sqlite3", result.FileName);
        }
        catch (Exception ex)
        {
            var fileName = $"AyazTekServis-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db";
            _db.BackupHistories.Add(new BackupHistory
            {
                CreatedAt = DateTime.Now,
                UserId = uid,
                Username = User.Identity?.Name ?? "Admin",
                FileName = fileName,
                IsSuccessful = false,
                ErrorMessage = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message
            });
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Yedek Hatası", "System", "Database",
                $"Veritabanı yedeği oluşturulamadı: {ex.GetType().Name}");
            TempData["ErrorMessage"] = "Veritabanı yedeği oluşturulamadı. Ayrıntı yedek geçmişine kaydedildi.";
            return RedirectToAction(nameof(Backups));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Backups()
    {
        ViewBag.LastSuccessful = await _db.BackupHistories.AsNoTracking().Where(x=>x.IsSuccessful).OrderByDescending(x=>x.CreatedAt).FirstOrDefaultAsync();
        return View(await _db.BackupHistories.AsNoTracking().OrderByDescending(x=>x.CreatedAt).Take(100).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Sessions(string? username = null, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = pageSize is 20 or 50 or 100 ? pageSize : 20;
        var now = DateTime.Now;

        var q = _db.UserSessions.AsNoTracking()
            .Where(x => x.RevokedAt == null && x.ExpiresAt > now);

        var activeSessions = await q.OrderByDescending(x => x.LastSeenAt).ToListAsync();
        if (!string.IsNullOrWhiteSpace(username))
        {
            var value = username.Trim();
            activeSessions = activeSessions.Where(x => ContainsIgnoreCaseTr(x.Username, value)).ToList();
        }
        var uniqueUsers = activeSessions
            .GroupBy(x => x.UserId)
            .Select(g => g.OrderByDescending(x => x.LastSeenAt).First())
            .OrderByDescending(x => x.LastSeenAt)
            .ToList();

        var total = uniqueUsers.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);
        var items = uniqueUsers.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        ViewBag.Username = username;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.Total = total;
        ViewBag.TotalPages = totalPages;
        return View(items);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeUserSessions(int userId)
    {
        var now = DateTime.Now;
        var sessions = await _db.UserSessions
            .Where(x => x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > now)
            .ToListAsync();
        if (sessions.Count == 0)
        {
            TempData["WarningMessage"] = "Bu kullanıcının aktif oturumu bulunamadı.";
            return RedirectToAction(nameof(Sessions));
        }

        foreach (var session in sessions) session.RevokedAt = now;
        await _db.SaveChangesAsync();
        var username = sessions[0].Username;
        await _audit.LogAsync("Oturumlar Sonlandırıldı", "UserSession", userId,
            $"{username} kullanıcısına ait {sessions.Count} aktif oturum yönetici tarafından sonlandırıldı.");
        TempData["SuccessMessage"] = $"{username} kullanıcısının aktif oturumları sonlandırıldı.";
        return RedirectToAction(nameof(Sessions));
    }

    [HttpGet]
    public async Task<IActionResult> DeletedRecords(int page=1)
    {
        page=Math.Max(1,page); const int size=20;
        var q=_db.ServiceRecords.AsNoTracking().Where(x=>x.IsDeleted);
        var total=await q.CountAsync(); var pages=Math.Max(1,(int)Math.Ceiling(total/(double)size)); page=Math.Min(page,pages);
        ViewBag.Page=page;ViewBag.TotalPages=pages;ViewBag.Total=total;
        return View(await q.OrderByDescending(x=>x.DeletedAt).Skip((page-1)*size).Take(size).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> ErrorLogs(int page=1)
    {
        page=Math.Max(1,page); const int size=20; var q=_db.ErrorLogs.AsNoTracking();
        var total=await q.CountAsync(); var pages=Math.Max(1,(int)Math.Ceiling(total/(double)size)); page=Math.Min(page,pages);
        ViewBag.Page=page;ViewBag.TotalPages=pages;ViewBag.Total=total;
        return View(await q.OrderByDescending(x=>x.CreatedAt).Skip((page-1)*size).Take(size).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearErrorLogs()
    {
        await _db.ErrorLogs.ExecuteDeleteAsync();
        await _audit.LogAsync("Hata Logları Temizlendi", "System", "ErrorLogs", "Yönetici hata loglarını temizledi.");
        TempData["SuccessMessage"]="Hata logları temizlendi."; return RedirectToAction(nameof(ErrorLogs));
    }

    [HttpGet]
    public async Task<IActionResult> ExportServices()
    {
        var data=await _db.ServiceRecords.AsNoTracking().OrderByDescending(x=>x.CreatedAt).ToListAsync();
        var rows=data.Select(x=>(IReadOnlyList<object?>)new object?[]{x.TrackingNumber,x.CreatedAt,x.ServiceSentDate,x.Sender,x.Brand,x.Model,x.ProductType,x.SerialNumber,x.SentToService,x.FaultReason,x.Status,x.IsUnderWarranty,x.ServiceResult,x.HasCharge,x.ChargeAmount,x.ChargeCurrency,x.ChargeVatMode,x.DeliveryMethod,x.DeliveryDate,x.CreatedByUsername,x.UpdatedByUsername,x.UpdatedAt,x.IsDeleted}).ToList();
        var bytes=XlsxExportService.Create("Servis Kayıtları",["Takip No","Kayıt Tarihi","Servise Gidiş Tarihi","Gönderen","Marka","Model","Cins","Seri No","Gönderilen Servis","Arıza Nedeni","Durum","Garantili","Yapılan İşlem","Ücret Var","Tutar","Para Birimi","KDV Durumu","Teslim Şekli","Teslim Tarihi","Kaydı Açan","Güncelleyen","Güncelleme Tarihi","Silindi"],rows);
        await _audit.LogAsync("Excel Dışa Aktarma","System","ServiceRecords","Servis kayıtları Excel olarak dışa aktarıldı.");
        return File(bytes,"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",$"ServisKayitlari-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportLogs()
    {
        var data=await _db.AuditLogs.AsNoTracking().OrderByDescending(x=>x.CreatedAt).ToListAsync();
        var rows=data.Select(x=>(IReadOnlyList<object?>)new object?[]{x.CreatedAt,x.Username,x.Action,x.EntityType,x.EntityId,x.Description,AuditChangeFormatter.DescribeInline(x.OldValues,x.NewValues),x.IpAddress}).ToList();
        var bytes=XlsxExportService.Create("İşlem Logları",["Tarih","Kullanıcı","İşlem","Varlık","Varlık ID","Açıklama","Değişiklik Detayları","IP"],rows);
        await _audit.LogAsync("Excel Dışa Aktarma","System","AuditLogs","İşlem logları Excel olarak dışa aktarıldı.");
        return File(bytes,"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",$"IslemLoglari-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportUserExcel(int id)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (user == null) return NotFound();
        var logs = await _db.AuditLogs.AsNoTracking().Where(x => x.UserId == id).OrderByDescending(x => x.CreatedAt).ToListAsync();
        var rows = logs.Select(x => (IReadOnlyList<object?>)new object?[] { x.CreatedAt, x.Action, x.EntityType, x.EntityId, x.Description, AuditChangeFormatter.DescribeInline(x.OldValues, x.NewValues), x.IpAddress }).ToList();
        if (rows.Count == 0) rows.Add(new object?[] { user.CreatedAt, "Kullanıcı Kaydı", "User", user.Id, $"{user.Username} · {user.Email}", "", "" });
        var bytes = XlsxExportService.Create($"{user.Username} İşlem Raporu",
            ["Tarih","İşlem","Varlık","Varlık ID","Açıklama","Değişiklik Detayları","IP"], rows);
        await _audit.LogAsync("Excel Dışa Aktarma", "User", user.Id, $"{user.Username} kullanıcı raporu Excel olarak dışa aktarıldı.");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Kullanici-{user.Username}-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> UserReport(int id)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (user == null) return NotFound();
        ViewBag.Logs = await _db.AuditLogs.AsNoTracking().Where(x => x.UserId == id)
            .OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync();
        return View(user);
    }

    [HttpGet]
    public async Task<IActionResult> ExportUsersExcel()
    {
        var data = await _db.Users.AsNoTracking().OrderBy(x => x.Username).ToListAsync();
        var rows = data.Select(x => (IReadOnlyList<object?>)new object?[]
        {
            x.Id, x.Username, x.Email, x.IsActive ? "Aktif" : "Pasif", x.IsAdmin ? "Admin" : "Kullanıcı", x.CreatedAt, x.LastLoginAt
        }).ToList();
        var bytes = XlsxExportService.Create("Kullanıcılar",
            ["ID","Kullanıcı Adı","E-posta","Durum","Rol","Kayıt Tarihi","Son Giriş"], rows);
        await _audit.LogAsync("Excel Dışa Aktarma", "User", "All", "Kullanıcı listesi Excel olarak dışa aktarıldı.");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Kullanicilar-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> UsersReport()
    {
        var data = await _db.Users.AsNoTracking().OrderBy(x => x.Username).ToListAsync();
        return View(data);
    }

    private async Task UpsertSetting(string key, string value)
    {
        var setting = await _db.SystemSettings.FindAsync(key);
        if (setting == null)
            _db.SystemSettings.Add(new SystemSetting { Key = key, Value = value, UpdatedAt = DateTime.Now, UpdatedBy = User.Identity?.Name ?? "Admin" });
        else
        {
            setting.Value = value; setting.UpdatedAt = DateTime.Now; setting.UpdatedBy = User.Identity?.Name ?? "Admin";
        }
    }
    private async Task<string?> BuildPublicResetUrlAsync(string email, string token)
    {
        var publicBaseUrl = await _db.SystemSettings.AsNoTracking()
            .Where(x => x.Key == "PublicBaseUrl")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
        publicBaseUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? _configuration["PublicBaseUrl"] : publicBaseUrl;
        publicBaseUrl = publicBaseUrl?.Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(publicBaseUrl) ||
            !Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            uri.IsLoopback ||
            string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            return null;

        return $"{publicBaseUrl}/Account/ResetPassword?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }

    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");
    private static bool ContainsIgnoreCaseTr(string? source, string? value) => TurkishCulture.CompareInfo.IndexOf(source ?? string.Empty, value ?? string.Empty, CompareOptions.IgnoreCase) >= 0;

}
