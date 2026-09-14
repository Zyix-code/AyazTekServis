using System.Security.Claims;
using System.Text.Json;
using AyazTekServis.Models;

namespace AyazTekServis.Services;

public class AuditService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _http;

    public AuditService(ApplicationDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task LogAsync(string action, string entityType, object? entityId, string description, object? oldValues = null, object? newValues = null)
    {
        var context = _http.HttpContext;
        int? userId = null;
        if (int.TryParse(context?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed)) userId = parsed;

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Username = context?.User.Identity?.Name ?? "Sistem",
            Action = action,
            EntityType = entityType,
            EntityId = entityId?.ToString() ?? string.Empty,
            Description = description,
            OldValues = oldValues == null ? string.Empty : JsonSerializer.Serialize(oldValues),
            NewValues = newValues == null ? string.Empty : JsonSerializer.Serialize(newValues),
            IpAddress = context?.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            CreatedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();
    }
}
