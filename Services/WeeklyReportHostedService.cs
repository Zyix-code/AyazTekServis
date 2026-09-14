using AyazTekServis.Models;
using Microsoft.EntityFrameworkCore;

namespace AyazTekServis.Services;

public class WeeklyReportHostedService(IServiceScopeFactory scopeFactory, ILogger<WeeklyReportHostedService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<WeeklyReportHostedService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TrySendWeeklyReportAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Haftalık rapor işlemi başarısız oldu.");
            }

            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }

    private async Task TrySendWeeklyReportAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        if (now.DayOfWeek != DayOfWeek.Monday || now.Hour < 9) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<EmailService>();
        var backup = scope.ServiceProvider.GetRequiredService<DatabaseBackupService>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();

        if (!email.IsConfigured) return;

        var lastSetting = await db.SystemSettings.FirstOrDefaultAsync(x => x.Key == "LastWeeklyReportSentAt", ct);
        if (lastSetting != null && DateTime.TryParse(lastSetting.Value, out var last) && last.Date >= now.Date.AddDays(-6)) return;

        var records = await db.ServiceRecords.AsNoTracking().Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var xlsx = XlsxExportService.Create(
            "Servis Kayıtları",
            ["Takip No", "Kayıt Tarihi", "Servise Gidiş Tarihi", "Gönderen", "Telefon", "E-posta", "Marka", "Model", "Cins", "Seri No", "Gönderilen Servis", "Durum", "Garanti", "Arıza", "Yapılan İşlem"],
            records.Select(x => (IReadOnlyList<object?>)[x.TrackingNumber, x.CreatedAt, x.ServiceSentDate, x.Sender, x.CustomerPhone, x.CustomerEmail, x.Brand, x.Model, x.ProductType, x.SerialNumber, x.SentToService, x.Status, x.IsUnderWarranty, x.FaultReason, x.ServiceResult]));

        var backupResult = await backup.CreateAsync(ct);
        db.BackupHistories.Add(new BackupHistory
        {
            CreatedAt = now,
            Username = "Sistem",
            FileName = backupResult.FileName,
            SizeBytes = backupResult.Bytes.LongLength,
            IsSuccessful = true
        });
        await db.SaveChangesAsync(ct);

        var adminEmails = await db.Users.AsNoTracking()
            .Where(x => x.IsActive && x.IsAdmin && x.Email != "")
            .Select(x => x.Email)
            .ToListAsync(ct);
        var managerSetting = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == "WeeklyReportRecipients", ct);
        var managerEmails = (managerSetting?.Value ?? "teknik@ayazteknoloji.com.tr")
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var recipients = adminEmails.Concat(managerEmails)
            .Where(x => new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (recipients.Count == 0) return;

        var attachments = new[]
        {
            new EmailAttachment("AyazTekServis-Haftalik-Servis-Raporu.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx),
            new EmailAttachment(backupResult.FileName, "application/x-sqlite3", backupResult.Bytes)
        };

        var subject = $"Ayaz Teknoloji - Haftalık Servis Raporu ({now:dd.MM.yyyy})";
        var body = MailTemplateService.Layout(
            "Haftalık servis raporu hazır",
            $"{now:dd.MM.yyyy} tarihli operasyon özeti ve veritabanı yedeği hazırlanmıştır.",
            $"""<table role="presentation" width="100%" cellspacing="0" cellpadding="0"><tr><td align="center"><div style="display:inline-block;min-width:220px;padding:18px 24px;border:1px solid #e3e8ef;border-radius:12px;background:#f8fafc;text-align:center"><div style="font-size:12px;color:#64748b;text-transform:uppercase;letter-spacing:.04em">Toplam servis kaydı</div><div style="font-size:28px;font-weight:800;color:#172033;margin-top:5px">{records.Count}</div></div><p style="font-size:14px;color:#475569;line-height:1.65;margin:18px auto 0;max-width:470px;text-align:center">Eklerde güncel servis Excel raporu ve veritabanı yedeği bulunmaktadır.</p></td></tr></table>""",
            "Haftalık Rapor",
            "#185c72");

        var success = true;
        foreach (var recipient in recipients)
        {
            var result = await email.SendAsync(recipient, subject, body, true, attachments, ct);
            success &= result.Success;
        }

        if (success)
        {
            if (lastSetting == null)
                db.SystemSettings.Add(new SystemSetting { Key = "LastWeeklyReportSentAt", Value = now.ToString("O"), UpdatedAt = now, UpdatedBy = "Sistem" });
            else
            {
                lastSetting.Value = now.ToString("O");
                lastSetting.UpdatedAt = now;
                lastSetting.UpdatedBy = "Sistem";
            }
            await db.SaveChangesAsync(ct);
            await audit.LogAsync("Haftalık Rapor Gönderildi", "System", "WeeklyReport", $"Haftalık rapor {recipients.Count} alıcıya gönderildi.");
        }
    }
}
