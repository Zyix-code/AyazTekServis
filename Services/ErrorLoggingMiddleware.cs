using System.Security.Claims;
using AyazTekServis.Models;

namespace AyazTekServis.Services;

public class ErrorLoggingMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory, ILogger<ErrorLoggingMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<ErrorLoggingMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        try { await _next(context); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled request error on {Method} {Path}", context.Request.Method, context.Request.Path);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                int? uid = int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : null;
                db.ErrorLogs.Add(new ErrorLog
                {
                    UserId = uid, Username = context.User.Identity?.Name ?? "Anonim", Method = context.Request.Method,
                    Path = context.Request.Path.Value ?? string.Empty, IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                    ExceptionType = ex.GetType().FullName ?? ex.GetType().Name, Message = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message,
                    StackTrace = (ex.StackTrace ?? string.Empty).Length > 12000 ? (ex.StackTrace ?? string.Empty)[..12000] : (ex.StackTrace ?? string.Empty), CreatedAt = DateTime.Now
                });
                await db.SaveChangesAsync();
            }
            catch (Exception logEx) { _logger.LogError(logEx, "Error log could not be persisted"); }
            throw;
        }
    }
}
