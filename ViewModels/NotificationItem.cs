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
}

public class NotificationCenterViewModel
{
    public List<NotificationItem> Items { get; set; } = [];
    public int CriticalCount => Items.Count(x => x.Bucket == "danger");
    public int WarningCount => Items.Count(x => x.Bucket == "warning");
    public int Count => Items.Count;
}
