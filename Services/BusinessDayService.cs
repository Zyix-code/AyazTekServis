namespace AyazTekServis.Services;

/// <summary>Şirket içi çalışma günü takibi; mevzuattaki yasal iş günü tanımı değildir.</summary>
public class BusinessDayService
{
    private static readonly HashSet<string> PreServiceStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Kayıt Açıldı", "Beklemede"
    };

    private static readonly HashSet<string> ClosedStatuses = new(StringComparer.OrdinalIgnoreCase) { "Tamamlandı" };

    private static readonly Dictionary<int, (DateTime RamadanEve, DateTime RamadanStart, DateTime SacrificeEve, DateTime SacrificeStart)> ReligiousHolidays = new()
    {
        [2025] = (new DateTime(2025, 3, 29), new DateTime(2025, 3, 30), new DateTime(2025, 6, 5), new DateTime(2025, 6, 6)),
        [2026] = (new DateTime(2026, 3, 19), new DateTime(2026, 3, 20), new DateTime(2026, 5, 26), new DateTime(2026, 5, 27)),
        [2027] = (new DateTime(2027, 3, 8), new DateTime(2027, 3, 9), new DateTime(2027, 5, 15), new DateTime(2027, 5, 16)),
        [2028] = (new DateTime(2028, 2, 25), new DateTime(2028, 2, 26), new DateTime(2028, 5, 4), new DateTime(2028, 5, 5))
    };

    public bool IsWorkingDay(DateTime date)
    {
        var d = date.Date;
        if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        if ((d.Month, d.Day) is (1, 1) or (4, 23) or (5, 1) or (5, 19) or
            (7, 15) or (8, 30) or (10, 28) or (10, 29)) return false;

        if (ReligiousHolidays.TryGetValue(d.Year, out var holiday))
        {
            if (d == holiday.RamadanEve || d == holiday.SacrificeEve) return false;
            if (d >= holiday.RamadanStart && d < holiday.RamadanStart.AddDays(3)) return false;
            if (d >= holiday.SacrificeStart && d < holiday.SacrificeStart.AddDays(4)) return false;
        }
        return true;
    }

    public int Count(DateTime start, DateTime? end = null)
    {
        var from = start.Date;
        var to = (end ?? DateTime.Today).Date;
        if (to <= from) return 0;

        var count = 0;
        for (var d = from.AddDays(1); d <= to; d = d.AddDays(1))
            if (IsWorkingDay(d)) count++;
        return count;
    }

    public bool IsPreService(string? status) => PreServiceStatuses.Contains(status ?? string.Empty);
    public int LimitFor(string? status) => IsPreService(status) ? 7 : 20;

    public string Describe(DateTime start, DateTime? end, string status)
    {
        var days = Count(start, end);
        var preService = IsPreService(status);
        if (ClosedStatuses.Contains(status))
            return $"Toplam takip süresi: {days} çalışma günü";

        if (preService)
            return days > 7
                ? $"{days} çalışma günü · servise gönderim hedefi {days - 7} gün aşıldı"
                : $"{days} çalışma günü · gönderim hedefine {7 - days} gün kaldı";

        return days switch
        {
            < 18 => $"{days} çalışma günü · servis süreci devam ediyor",
            < 20 => $"{days} çalışma günü · 20 günlük takip hedefine {20 - days} gün kaldı",
            20 => "20 çalışma günü · takip hedefi bugün doldu",
            _ => $"{days} çalışma günü · takip hedefi {days - 20} gün aşıldı"
        };
    }

    public string Level(DateTime start, DateTime? end, string status)
    {
        if (ClosedStatuses.Contains(status)) return "success";
        var days = Count(start, end);
        if (IsPreService(status)) return days > 7 ? "danger" : "info";
        if (days >= 20) return "danger";
        if (days >= 18) return "warning";
        return "info";
    }

    public DateTime StartFor(AyazTekServis.Models.ServiceRecord record) =>
        IsPreService(record.Status) ? record.CreatedAt.Date : (record.ServiceSentDate ?? record.ServiceArrivalDate).Date;

    public int Count(AyazTekServis.Models.ServiceRecord record) => Count(StartFor(record), record.DeliveryDate);
    public string Describe(AyazTekServis.Models.ServiceRecord record) => record.DeliveryDate.HasValue ? $"Toplam takip süresi: {Count(record)} çalışma günü" : Describe(StartFor(record), null, record.Status);
    public string Level(AyazTekServis.Models.ServiceRecord record) => record.DeliveryDate.HasValue ? "success" : Level(StartFor(record), null, record.Status);
}
