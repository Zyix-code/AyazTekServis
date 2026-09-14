namespace AyazTekServis.Services;

public class BusinessDayService
{
    private static readonly HashSet<string> PreServiceStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Kayıt Açıldı", "Beklemede"
    };

    private static readonly HashSet<string> ClosedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Tamamlandı", "İade Edildi", "Onarılamadı"
    };

    public int Count(DateTime start, DateTime? end = null)
    {
        var from = start.Date;
        var to = (end ?? DateTime.Today).Date;
        if (to < from) return 0;

        var count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                count++;

        return Math.Max(0, count - 1);
    }

    public bool IsPreService(string? status) =>
        PreServiceStatuses.Contains(status ?? string.Empty);

    public int LimitFor(string? status) => IsPreService(status) ? 7 : 20;

    public string Describe(DateTime start, DateTime? end, string status)
    {
        var days = Count(start, end);
        var preService = IsPreService(status);
        var closed = ClosedStatuses.Contains(status);

        if (closed)
        {
            var limit = preService ? 7 : 20;
            return days > limit
                ? $"Toplam süre: {days} iş günü · {limit} iş günü hedefi {days - limit} iş günü aşıldı"
                : $"Toplam süre: {days} iş günü";
        }

        if (preService)
        {
            return days switch
            {
                0 => "0 iş günü · kayıt yeni açıldı",
                <= 2 => $"{days} iş günü · servis öncesi bekliyor",
                <= 7 => $"{days} iş günü · servis öncesi bekliyor, 7 iş günü sınırına {7 - days} iş günü kaldı",
                8 => "8 iş günü · cihaz hâlâ servise gönderilmedi, servis öncesi süre aşıldı",
                _ => $"{days} iş günü · cihaz hâlâ servise gönderilmedi, 7 iş günü sınırı {days - 7} iş günü aşıldı"
            };
        }

        return days switch
        {
            <= 7 => $"{days} iş günü · servis süreci devam ediyor",
            < 20 => $"{days} iş günü · yasal 20 iş günü sınırına {20 - days} iş günü kaldı",
            20 => "20 iş günü · yasal servis süresi bugün doldu",
            _ => $"{days} iş günü · yasal servis süresi {days - 20} iş günü aşıldı"
        };
    }

    public string Level(DateTime start, DateTime? end, string status)
    {
        if (ClosedStatuses.Contains(status)) return "success";

        var days = Count(start, end);
        if (IsPreService(status))
        {
            return days >= 8 ? "danger" : "info";
        }
        if (days >= 20) return "danger";
        if (days >= 18) return "warning";
        return "info";
    }
    public DateTime StartFor(AyazTekServis.Models.ServiceRecord record)
    {
        if (IsPreService(record.Status)) return record.CreatedAt.Date;
        return (record.ServiceSentDate ?? record.ServiceArrivalDate).Date;
    }

    public int Count(AyazTekServis.Models.ServiceRecord record) => Count(StartFor(record), record.DeliveryDate);

    public string Describe(AyazTekServis.Models.ServiceRecord record) => Describe(StartFor(record), record.DeliveryDate, record.Status);

    public string Level(AyazTekServis.Models.ServiceRecord record) => Level(StartFor(record), record.DeliveryDate, record.Status);

}
