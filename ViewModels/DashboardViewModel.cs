using AyazTekServis.Models;

namespace AyazTekServis.ViewModels;

public class DashboardViewModel
{
    public int Total { get; set; }
    public int Open { get; set; }
    public int InService { get; set; }
    public int Waiting { get; set; }
    public int Ready { get; set; }
    public int Completed { get; set; }
    public int UnderWarranty { get; set; }
    public IReadOnlyList<ServiceRecord> Recent { get; set; } = Array.Empty<ServiceRecord>();
}
