using AyazTekServis.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using MimeKit.Utils;

namespace AyazTekServis.Services;

public record EmailSendResult(bool Success, string Message);
public record EmailAttachment(string FileName, string ContentType, byte[] Content);

public class EmailService
{
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<EmailService> _logger;
    private readonly SecretProtectionService _secrets;
    private readonly IWebHostEnvironment _environment;
    private Dictionary<string, string>? _settingsCache;

    public EmailService(IConfiguration configuration, ApplicationDbContext db, ILogger<EmailService> logger, SecretProtectionService secrets, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _db = db;
        _logger = logger;
        _secrets = secrets;
        _environment = environment;
    }

    private IReadOnlyDictionary<string, string> Settings =>
        _settingsCache ??= _db.SystemSettings.AsNoTracking().ToDictionary(x => x.Key, x => x.Value);

    private string Setting(string dbKey, string configKey, string fallback = "") =>
        Settings.TryGetValue(dbKey, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : _configuration[configKey] ?? fallback;

    private string Password
    {
        get
        {
            if (Settings.TryGetValue("SmtpPasswordProtected", out var protectedValue))
            {
                var unprotected = _secrets.Unprotect(protectedValue);
                if (!string.IsNullOrWhiteSpace(unprotected)) return unprotected;
            }
            return _configuration["Smtp:Password"] ?? Environment.GetEnvironmentVariable("Smtp__Password") ?? string.Empty;
        }
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Setting("SmtpHost", "Smtp:Host")) &&
        !string.IsNullOrWhiteSpace(Setting("SmtpFrom", "Smtp:From")) &&
        !string.IsNullOrWhiteSpace(Password);

    public Task<EmailSendResult> SendAsync(string to, string subject, string body, bool isHtml = true) =>
        SendAsync(to, subject, body, isHtml, null, CancellationToken.None);

    public async Task<EmailSendResult> SendAsync(
        string to,
        string subject,
        string body,
        bool isHtml,
        IEnumerable<EmailAttachment>? attachments,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return new(false, "E-posta gönderimi için SMTP parolası henüz tanımlanmamış.");

        try
        {
            var host = Setting("SmtpHost", "Smtp:Host", "smtp.hostinger.com");
            var from = Setting("SmtpFrom", "Smtp:From", "teknik@ayazteknoloji.com.tr");
            var port = int.TryParse(Setting("SmtpPort", "Smtp:Port", "465"), out var parsedPort) ? parsedPort : 465;
            var user = Setting("SmtpUsername", "Smtp:Username", from);
            var displayName = Setting("CompanyName", "Smtp:DisplayName", "AyazTek Servis");

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(displayName, from));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            var builder = new BodyBuilder();
            if (isHtml)
            {
                if (body.Contains("__AYAZ_LOGO__", StringComparison.Ordinal))
                {
                    var logoPath = Path.Combine(_environment.WebRootPath, "images", "ayaz-teknoloji-logo.jpg");
                    if (File.Exists(logoPath))
                    {
                        var logo = builder.LinkedResources.Add(logoPath);
                        logo.ContentId = MimeKit.Utils.MimeUtils.GenerateMessageId();
                        body = body.Replace("__AYAZ_LOGO__", $"cid:{logo.ContentId}", StringComparison.Ordinal);
                    }
                    else
                    {
                        body = body.Replace("__AYAZ_LOGO__", string.Empty, StringComparison.Ordinal);
                    }
                }
                builder.HtmlBody = body;
            }
            else builder.TextBody = body;

            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    var contentType = ContentType.Parse(attachment.ContentType);
                    builder.Attachments.Add(attachment.FileName, attachment.Content, contentType);
                }
            }
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            client.Timeout = 30000;
            var socketOptions = port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(host, port, socketOptions, cancellationToken);
            if (!string.IsNullOrWhiteSpace(user))
                await client.AuthenticateAsync(user, Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return new(true, "E-posta gönderildi.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "E-posta gönderilemedi: {Recipient}", to);
            return new(false, "E-posta gönderilemedi. SMTP bağlantısını ve hesap bilgilerini kontrol edin.");
        }
    }
}
