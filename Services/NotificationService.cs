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
    private static readonly string[] ClosedStatuses = ["Tamamlandı", "İade Edildi", "Onarılamadı"];

    public async Task<NotificationCenterViewModel> GetAsync(bool isAdmin, int maxItems = 100)
    {
        var key = $"notifications:{(isAdmin ? "admin" : "user")}";
        var cached = await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await BuildItemsAsync(isAdmin);
        }) ?? [];

        return new NotificationCenterViewModel
        {
            Items = cached.Take(Math.Clamp(maxItems, 1, 100)).Select(Clone).ToList()
        };
    }

    private async Task<List<NotificationItem>> BuildItemsAsync(bool isAdmin)
    {
        var items = new List<NotificationItem>();
        var records = await _db.ServiceRecords.AsNoTracking()
            .Where(x => !x.IsDeleted && !ClosedStatuses.Contains(x.Status))
            .Select(x => new { x.Id, x.TrackingNumber, x.Brand, x.Model, x.Status, x.ServiceArrivalDate, x.ServiceSentDate, x.UpdatedAt, x.CreatedAt })
            .ToListAsync();

        foreach (var r in records)
        {
            var preService = _businessDays.IsPreService(r.Status);
            var start = preService ? r.CreatedAt : (r.ServiceSentDate ?? r.ServiceArrivalDate);
            var days = _businessDays.Count(start);
            var approvalWaiting = string.Equals(r.Status, "Müşteri Onayı Bekliyor", StringComparison.OrdinalIgnoreCase);
            if (!preService && !approvalWaiting && days < 18) continue;

            var level = approvalWaiting && days < 20 ? "warning" : _businessDays.Level(start, null, r.Status);
            var bucket = preService ? "preservice" : (days >= 20 ? "danger" : "warning");
            var title = preService
                ? (days >= 8 ? "Servise gönderilmedi · süre aşıldı" : "Servise gönderilmeyi bekliyor · 0-7 iş günü")
                : approvalWaiting
                    ? "Müşteri onayı bekleniyor"
                    : days >= 20 ? "20+ iş günü · yasal süre kritik" : "18-19 iş günü · yasal sınıra yaklaşıyor";

            items.Add(new NotificationItem
            {
                Type = level, Bucket = bucket,
                Icon = level == "danger" ? "bi-exclamation-octagon-fill" : level == "warning" ? "bi-exclamation-triangle-fill" : "bi-clock-history",
                Title = $"{r.TrackingNumber} · {title}",
                Message = $"{r.Brand} {r.Model} · {_businessDays.Describe(start, null, r.Status)} · Durum: {r.Status}",
                Url = $"/Service/Details/{r.Id}",
                SortDate = r.UpdatedAt ?? r.CreatedAt
            });
        }

        if (isAdmin)
        {
            var lastBackup = await _db.BackupHistories.AsNoTracking().Where(x => x.IsSuccessful)
                .OrderByDescending(x => x.CreatedAt).Select(x => (DateTime?)x.CreatedAt).FirstOrDefaultAsync();
            var age = lastBackup == null ? int.MaxValue : (DateTime.Now - lastBackup.Value).Days;
            if (age >= 7)
            {
                items.Add(new NotificationItem
                {
                    Type = "warning", Bucket = "system", Icon = "bi-database-exclamation", Title = "Veritabanı yedeği gecikti",
                    Message = lastBackup == null ? "Henüz başarılı bir veritabanı yedeği alınmamış." : $"Son başarılı yedek {age} gün önce alındı.",
                    Url = "/Admin/Backups", SortDate = DateTime.Now
                });
            }

            var errorCount = await _db.ErrorLogs.AsNoTracking().CountAsync(x => x.CreatedAt >= DateTime.Now.AddDays(-1));
            if (errorCount > 0)
            {
                items.Add(new NotificationItem
                {
                    Type = "danger", Bucket = "system", Icon = "bi-bug-fill", Title = "Son 24 saatte uygulama hataları",
                    Message = $"{errorCount} uygulama hatası kaydedildi. Hata loglarını inceleyin.",
                    Url = "/Admin/ErrorLogs", SortDate = DateTime.Now
                });
            }
        }

        return items.OrderByDescending(x => x.Type == "danger")
            .ThenByDescending(x => x.Type == "warning")
            .ThenByDescending(x => x.SortDate)
            .Take(100).ToList();
    }

    private static NotificationItem Clone(NotificationItem x) => new()
    {
        Type = x.Type, Bucket = x.Bucket, Icon = x.Icon, Title = x.Title, Message = x.Message, Url = x.Url, SortDate = x.SortDate
    };
}
