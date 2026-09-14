using AyazTekServis.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AyazTekServis.Controllers;

[Authorize]
public class NotificationsController(NotificationService notifications) : Controller
{
    private readonly NotificationService _notifications = notifications;

    public async Task<IActionResult> Index(string? type, int page = 1)
    {
        page = Math.Max(1, page);
        var all = await _notifications.GetAsync(User.IsInRole("Admin"), 100);
        var allItems = all.Items.ToList();
        var filtered = string.IsNullOrWhiteSpace(type)
            ? allItems
            : allItems.Where(x => string.Equals(x.Bucket, type, StringComparison.OrdinalIgnoreCase)).ToList();

        ViewBag.AllTotal = allItems.Count;
        ViewBag.PreServiceTotal = allItems.Count(x => x.Bucket == "preservice");
        ViewBag.WarningTotal = allItems.Count(x => x.Bucket == "warning");
        ViewBag.CriticalTotal = allItems.Count(x => x.Bucket == "danger");

        const int pageSize = 15;
        var total = filtered.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);

        all.Items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        ViewBag.Type = type;
        ViewBag.Page = page;
        ViewBag.Total = total;
        ViewBag.TotalPages = totalPages;
        return View(all);
    }
}
