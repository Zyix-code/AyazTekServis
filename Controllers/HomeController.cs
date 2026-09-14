using AyazTekServis.Models;
using AyazTekServis.ViewModels;
using AyazTekServis.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AyazTekServis.Controllers;

[Authorize]
public class HomeController(ApplicationDbContext db, BusinessDayService businessDays) : Controller
{
    private readonly ApplicationDbContext _db = db;
    private readonly BusinessDayService _businessDays = businessDays;

    public async Task<IActionResult> Index()
    {
        var q = _db.ServiceRecords.AsNoTracking().Where(x => !x.IsDeleted);
        var stats = await q.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Count(),
            Open = g.Count(x => x.Status == "Kayıt Açıldı"),
            InService = g.Count(x => x.Status == "Serviste" || x.Status == "Onarımda"),
            Waiting = g.Count(x => x.Status == "Beklemede" || x.Status == "Parça Bekliyor" || x.Status == "Müşteri Onayı Bekliyor"),
            Ready = g.Count(x => x.Status == "Teslime Hazır"),
            Completed = g.Count(x => x.Status == "Tamamlandı" || x.Status == "İade Edildi" || x.Status == "Onarılamadı"),
            UnderWarranty = g.Count(x => x.IsUnderWarranty)
        }).SingleOrDefaultAsync();

        var openItems = await q.Where(x => x.Status != "Tamamlandı" && x.Status != "İade Edildi" && x.Status != "Onarılamadı")
            .Select(x => new { x.ServiceArrivalDate, x.ServiceSentDate, x.CreatedAt, x.DeliveryDate, x.Status }).ToListAsync();

        ViewBag.Overdue20 = openItems.Count(x =>
            !_businessDays.IsPreService(x.Status) && _businessDays.Count(x.ServiceSentDate ?? x.ServiceArrivalDate) >= 20);
        ViewBag.Warning18To19 = openItems.Count(x =>
            !_businessDays.IsPreService(x.Status) && _businessDays.Count(x.ServiceSentDate ?? x.ServiceArrivalDate) >= 18 && _businessDays.Count(x.ServiceSentDate ?? x.ServiceArrivalDate) <= 19);
        ViewBag.PreService0To7 = openItems.Count(x =>
            _businessDays.IsPreService(x.Status) && _businessDays.Count(x.CreatedAt) <= 7);
        ViewBag.PreServiceOverdue = openItems.Count(x =>
            _businessDays.IsPreService(x.Status) && _businessDays.Count(x.CreatedAt) >= 8);

        var model = new DashboardViewModel
        {
            Total = stats?.Total ?? 0, Open = stats?.Open ?? 0, InService = stats?.InService ?? 0,
            Waiting = stats?.Waiting ?? 0, Ready = stats?.Ready ?? 0, Completed = stats?.Completed ?? 0,
            UnderWarranty = stats?.UnderWarranty ?? 0,
            Recent = await q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(8).ToListAsync()
        };
        return View(model);
    }
}
