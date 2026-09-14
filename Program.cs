using AyazTekServis.Models;
using AyazTekServis.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var configuredUrl = builder.Configuration["AppUrl"];
if (!string.IsNullOrWhiteSpace(configuredUrl))
{
    builder.WebHost.UseUrls(configuredUrl);
}
else if (builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls("http://127.0.0.1:5140");
}
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDataProtection();
builder.Services.AddMemoryCache();
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
}

builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")), poolSize: 64);

builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<LoginAttemptService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddSingleton<SecretProtectionService>();
builder.Services.AddSingleton<DatabaseBackupService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddSingleton<ServiceNumberGenerator>();
builder.Services.AddSingleton<BusinessDayService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddHostedService<WeeklyReportHostedService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "AyazTek.Auth.v7";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                var sid = context.Principal?.FindFirst("session_id")?.Value;
                if (string.IsNullOrWhiteSpace(sid)) return;
                var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                var session = await db.UserSessions.FirstOrDefaultAsync(x => x.SessionId == sid);
                if (session == null || session.RevokedAt != null || session.ExpiresAt <= DateTime.Now)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }
                if (session.LastSeenAt < DateTime.Now.AddMinutes(-5))
                {
                    session.LastSeenAt = DateTime.Now;
                    session.ExpiresAt = DateTime.Now.AddHours(8);
                    await db.SaveChangesAsync();
                }
            }
        };
    });

builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
    context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
    context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
    context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
    context.Response.Headers["Origin-Agent-Cluster"] = "?1";
    var connectSrc = app.Environment.IsDevelopment()
        ? "connect-src 'self' http://localhost:* ws://localhost:*;"
        : "connect-src 'self';";
    context.Response.Headers.ContentSecurityPolicy = $"default-src 'self'; base-uri 'self'; form-action 'self'; object-src 'none'; frame-ancestors 'none'; style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; script-src 'self' 'unsafe-inline'; font-src 'self' https://cdn.jsdelivr.net data:; img-src 'self' data:; {connectSrc}";
    await next();
});

app.UseMiddleware<ErrorLoggingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseResponseCompression();
}
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();
    await DatabaseInitializer.InitializeAsync(db, passwordService);
}

app.Run();
