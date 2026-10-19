using AyazTekServis.Models;
using AyazTekServis.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AyazTekServis.Services;

public class NotificationService(ApplicationDbContext db, BusinessDayService businessDays, IMemoryCache cache)
{
    private readonly ApplicationDbContext _db = db;
    private readonly BusinessDayService _businessDays = businessDays;
    private readonly IMemoryCache _cache = cache;

    public async Task<NotificationCenterViewModel> GetAsync(bool isAdmin, int maxItems = 500)
    {
        var key = $"notifications:v2:{(isAdmin ? "admin" : "user")}";
        var cached = await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await BuildItemsAsync(isAdmin);
        }) ?? [];

        return new NotificationCenterViewModel
        {
            Items = cached.Take(Math.Clamp(maxItems, 1, 500)).Select(Clone).ToList()
        };
    }

    private async Task<List<NotificationItem>> BuildItemsAsync(bool isAdmin)
    {
        var items = new List<NotificationItem>();
        var records = await _db.ServiceRecords.AsNoTracking()
            .Where(x => !x.IsDeleted && x.DeliveryDate == null && x.Status != "Tamamlandı")
            .Select(x => new
            {
                x.Id, x.TrackingNumber, x.Brand, x.Model, x.SerialNumber,
                x.Sender, x.SentToService, x.Status, x.ServiceArrivalDate,
                x.ServiceSentDate, x.UpdatedAt, x.CreatedAt
            })
            .ToListAsync();

        foreach (var r in records)
        {
            var preService = _businessDays.IsPreService(r.Status);
            var start = preService ? r.CreatedAt : (r.ServiceSentDate ?? r.ServiceArrivalDate);
            var days = _businessDays.Count(start);
            var approvalWaiting = string.Equals(r.Status, "Müşteri Onayı Bekliyor", StringComparison.OrdinalIgnoreCase);
            var isOverdue = preService ? days > 7 : days >= 20;
            var isNear = !preService && days >= 18 && days < 20;

            if (!preService && !approvalWaiting && !isOverdue && !isNear) continue;

            var bucket = preService ? "preservice" : isOverdue ? "danger" : isNear ? "warning" : "action";
            var level = isOverdue ? "danger" : isNear || approvalWaiting ? "warning" : "info";
            var title = preService
                ? (isOverdue ? "Servise gönderim hedefi aşıldı" : "Servise gönderilmeyi bekliyor")
                : isOverdue ? "Servis takip süresi aşıldı"
                : isNear ? "Servis takip süresi yaklaşıyor"
                : "Müşteri onayı bekleniyor";

            items.Add(new NotificationItem
            {
                Type = level,
                Bucket = bucket,
                Icon = isOverdue ? "bi-exclamation-octagon-fill" :
                    isNear || approvalWaiting ? "bi-exclamation-triangle-fill" : "bi-clock-history",
                Title = title,
                Message = _businessDays.Describe(start, null, r.Status),
                Url = $"/Service/Details/{r.Id}",
                SortDate = r.UpdatedAt ?? r.CreatedAt,
                IsServiceRecord = true,
                Device = $"{r.Brand} {r.Model}".Trim(),
                SerialNumber = r.SerialNumber,
                Sender = r.Sender,
                SentToService = r.SentToService,
                Status = r.Status,
                TrackingNumber = r.TrackingNumber,
                RegisteredAt = r.CreatedAt,
                SentAt = r.ServiceSentDate,
                LastUpdatedAt = r.UpdatedAt,
                WorkingDays = days
            });
        }

        if (isAdmin)
        {
            var lastBackup = await _db.BackupHistories.AsNoTracking().Where(x => x.IsSuccessful)
                .OrderByDescending(x => x.CreatedAt).Select(x => (DateTime?)x.CreatedAt).FirstOrDefaultAsync();
            var age = lastBackup == null ? int.MaxValue : (DateTime.Now - lastBackup.Value).Days;
            if (age >= 7)
                items.Add(new NotificationItem
                {
                    Type = "warning", Bucket = "system", Icon = "bi-database-exclamation", Title = "Veritabanı yedeği gecikti",
                    Message = lastBackup == null ? "Henüz başarılı bir veritabanı yedeği alınmamış." : $"Son başarılı yedek {age} gün önce alındı.",
                    Url = "/Admin/Backups", SortDate = DateTime.Now
                });

            var errorCount = await _db.ErrorLogs.AsNoTracking().CountAsync(x => x.CreatedAt >= DateTime.Now.AddDays(-1));
            if (errorCount > 0)
                items.Add(new NotificationItem
                {
                    Type = "danger", Bucket = "system", Icon = "bi-bug-fill", Title = "Son 24 saatte uygulama hataları",
                    Message = $"{errorCount} uygulama hatası kaydedildi. Hata loglarını inceleyin.",
                    Url = "/Admin/ErrorLogs", SortDate = DateTime.Now
                });
        }

        return items.OrderByDescending(x => x.Type == "danger")
            .ThenByDescending(x => x.Type == "warning")
            .ThenByDescending(x => x.WorkingDays)
            .ThenByDescending(x => x.SortDate)
            .Take(500).ToList();
    }

    private static NotificationItem Clone(NotificationItem x) => new()
    {
        Type = x.Type, Bucket = x.Bucket, Icon = x.Icon, Title = x.Title,
        Message = x.Message, Url = x.Url, SortDate = x.SortDate,
        IsServiceRecord = x.IsServiceRecord, Device = x.Device,
        SerialNumber = x.SerialNumber, Sender = x.Sender, SentToService = x.SentToService,
        Status = x.Status, TrackingNumber = x.TrackingNumber, RegisteredAt = x.RegisteredAt,
        SentAt = x.SentAt, LastUpdatedAt = x.LastUpdatedAt, WorkingDays = x.WorkingDays
    };
}
