namespace AyazTekServis.ViewModels;

public class NotificationItem
{
    public string Type { get; set; } = "info";
    public string Bucket { get; set; } = "info";
    public string Icon { get; set; } = "bi-info-circle";
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime SortDate { get; set; } = DateTime.Now;
    public bool IsServiceRecord { get; set; }
    public string Device { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string SentToService { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
    public int WorkingDays { get; set; }
}

public class NotificationCenterViewModel
{
    public List<NotificationItem> Items { get; set; } = [];
    public int CriticalCount => Items.Count(x => x.Bucket == "danger");
    public int WarningCount => Items.Count(x => x.Bucket == "warning");
    public int Count => Items.Count;
}
