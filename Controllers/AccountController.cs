using System.Globalization;
using System.Security.Claims;
using AyazTekServis.Models;
using AyazTekServis.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AyazTekServis.Controllers;

public class AccountController : Controller
{
    private static readonly string[] SecurityQuestions = [
        "İlk evcil hayvanınızın adı nedir?",
        "İlkokul öğretmeninizin soyadı nedir?",
        "Çocukken yaşadığınız mahallenin adı nedir?",
        "En sevdiğiniz çocukluk arkadaşınızın adı nedir?",
        "İlk sahip olduğunuz aracın/telefonun markası nedir?"
    ];
    private readonly ApplicationDbContext _context;
    private readonly PasswordService _passwords;
    private readonly TokenService _tokens;
    private readonly EmailService _email;
    private readonly AuditService _audit;
    private readonly LoginAttemptService _loginAttempts;
    private readonly IConfiguration _configuration;

    public AccountController(ApplicationDbContext context, PasswordService passwords, TokenService tokens, EmailService email, AuditService audit, LoginAttemptService loginAttempts, IConfiguration configuration)
    {
        _context = context;
        _passwords = passwords;
        _tokens = tokens;
        _email = email;
        _audit = audit;
        _loginAttempts = loginAttempts;
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string username, string password)
    {
        username = (username ?? string.Empty).Trim();
        var attemptKey = $"{HttpContext.Connection.RemoteIpAddress}|{username.ToUpper(TurkishCulture)}";
        if (_loginAttempts.IsBlocked(attemptKey))
        {
            ViewBag.Error = "Çok fazla başarısız giriş denemesi yapıldı. Birkaç dakika sonra tekrar deneyin.";
            return View();
        }

        var loginUsers = await _context.Users.ToListAsync();
        var user = loginUsers.FirstOrDefault(u => EqualsIgnoreCaseTr(u.Username, username));
        if (user == null || !_passwords.Verify(password ?? string.Empty, user.Password))
        {
            _loginAttempts.RegisterFailure(attemptKey);
            ViewBag.Error = "Kullanıcı adı veya şifre hatalı.";
            return View();
        }

        if (!user.IsActive)
        {
            ViewBag.Error = "Hesabınız henüz yönetici tarafından onaylanmamış veya pasif durumda.";
            return View();
        }

        if (!_passwords.IsHashed(user.Password))
        {
            var passwordToHash = !string.IsNullOrWhiteSpace(password)
                ? password
                : user.Password;

            if (!string.IsNullOrWhiteSpace(passwordToHash))
            {
                user.Password = _passwords.Hash(passwordToHash);
            }
        }
        _loginAttempts.Reset(attemptKey);
        user.LastLoginAt = DateTime.Now;
        await _context.SaveChangesAsync();
        var loginNow = DateTime.Now;
        var previousActiveSessions = await _context.UserSessions
            .Where(x => x.UserId == user.Id && x.RevokedAt == null && x.ExpiresAt > loginNow)
            .ToListAsync();
        foreach (var previousSession in previousActiveSessions) previousSession.RevokedAt = loginNow;

        var sessionId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        _context.UserSessions.Add(new UserSession
        {
            UserId = user.Id, Username = user.Username, SessionId = sessionId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            UserAgent = (Request.Headers.UserAgent.ToString().Length > 600 ? Request.Headers.UserAgent.ToString()[..600] : Request.Headers.UserAgent.ToString()),
            StartedAt = loginNow, LastSeenAt = loginNow, ExpiresAt = loginNow.AddHours(8)
        });
        await _context.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.IsAdmin ? "Admin" : "User"),
            new("session_id", sessionId)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });
        HttpContext.User = principal;
        await _audit.LogAsync("Giriş", "User", user.Id, $"{user.Username} sisteme giriş yaptı.");
        return RedirectToAction("Index", "Home");
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var sid = User.FindFirstValue("session_id");
        if (!string.IsNullOrWhiteSpace(sid))
        {
            var session = await _context.UserSessions.FirstOrDefaultAsync(x => x.SessionId == sid && x.RevokedAt == null);
            if (session != null) { session.RevokedAt = DateTime.Now; await _context.SaveChangesAsync(); }
        }
        await _audit.LogAsync("Çıkış", "User", User.FindFirstValue(ClaimTypes.NameIdentifier), "Kullanıcı sistemden çıkış yaptı.");
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        ViewBag.SecurityQuestions = SecurityQuestions;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(string username, string email, string password, string confirmPassword, string securityQuestion, string securityAnswer)
    {
        username = (username ?? string.Empty).Trim();
        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        password ??= string.Empty;
        confirmPassword ??= string.Empty;
        securityQuestion = (securityQuestion ?? string.Empty).Trim();
        securityAnswer = (securityAnswer ?? string.Empty).Trim();

        if (username.Length < 3 || username.Length > 80)
            ViewBag.Error = "Kullanıcı adı 3-80 karakter olmalıdır.";
        else if (email.Length > 180 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            ViewBag.Error = "Geçerli bir e-posta adresi girin.";
        else if (password.Length > 256 || confirmPassword.Length > 256)
            ViewBag.Error = "Şifre en fazla 256 karakter olabilir.";
        else if (password != confirmPassword)
            ViewBag.Error = "Şifreler eşleşmiyor.";
        else if (!_passwords.IsStrongEnough(password))
            ViewBag.Error = "Şifre en az 6 karakter; büyük/küçük harf, rakam ve özel karakter içermelidir.";
        else if (!SecurityQuestions.Contains(securityQuestion))
            ViewBag.Error = "Geçerli bir güvenlik sorusu seçin.";
        else if (securityAnswer.Length < 2 || securityAnswer.Length > 200)
            ViewBag.Error = "Güvenlik sorusu cevabı 2-200 karakter olmalıdır.";
        else if (await _context.Users.AnyAsync(u => EF.Functions.Collate(u.Email, "NOCASE") == email) ||
                 (await _context.Users.AsNoTracking().Select(u => u.Username).ToListAsync()).Any(x => EqualsIgnoreCaseTr(x, username)))
            ViewBag.Error = "Bu kullanıcı adı veya e-posta zaten kayıtlı.";

        if (ViewBag.Error != null)
        {
            ViewBag.Username = username;
            ViewBag.Email = email;
            ViewBag.SelectedSecurityQuestion = securityQuestion;
            ViewBag.SecurityQuestions = SecurityQuestions;
            return View();
        }

        var user = new User
        {
            Username = username,
            Email = email,
            Password = _passwords.Hash(password),
            SecurityQuestion = securityQuestion,
            SecurityAnswer = HashSecurityAnswer(securityAnswer),
            IsActive = false,
            IsAdmin = false,
            CreatedAt = DateTime.Now
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await _audit.LogAsync("Kayıt Başvurusu", "User", user.Id, $"Yeni kullanıcı başvurusu: {user.Username}");
        await _email.SendAsync(user.Email, "Ayaz Teknoloji - Kayıt Başvurusu",
            MailTemplateService.Account(
                "Kayıt başvurunuz alındı",
                $"Merhaba {user.Username}, hesabınız oluşturuldu ve yönetici onayı bekliyor.",
                """<div style="padding:14px 16px;border-radius:12px;background:#f8fafc;border:1px solid #e3e8ef;color:#475569;font-size:14px;line-height:1.6">Hesabınız aktif hale getirildiğinde giriş yapabilirsiniz.</div>""",
                "Yönetici Onayı Bekleniyor"));

        TempData["SuccessMessage"] = "Kayıt başvurunuz alındı. Yönetici onayından sonra giriş yapabilirsiniz.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult ForgotPassword() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(string email, string? securityAnswer)
    {
        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user != null && user.IsActive && !string.IsNullOrWhiteSpace(user.SecurityQuestion))
        {
            ViewBag.Email = email;
            ViewBag.SecurityQuestion = user.SecurityQuestion;
            if (string.IsNullOrWhiteSpace(securityAnswer))
            {
                ViewBag.Step = 2;
                return View();
            }
            var resetAttemptKey = $"reset|{HttpContext.Connection.RemoteIpAddress}|{user.Id}";
            if ((securityAnswer?.Length ?? 0) > 200)
            {
                _loginAttempts.RegisterFailure(resetAttemptKey);
                ViewBag.Step = 2;
                ViewBag.Error = "Güvenlik sorusu cevabı doğrulanamadı.";
                return View();
            }
            if (_loginAttempts.IsBlocked(resetAttemptKey))
            {
                ViewBag.Step = 2;
                ViewBag.Error = "Çok fazla başarısız doğrulama denemesi yapıldı. Birkaç dakika sonra tekrar deneyin.";
                return View();
            }
            if (!VerifySecurityAnswer(securityAnswer, user.SecurityAnswer))
            {
                _loginAttempts.RegisterFailure(resetAttemptKey);
                await _audit.LogAsync("Güvenlik Sorusu Hatası", "User", user.Id, "Şifre sıfırlamada güvenlik sorusu yanlış cevaplandı.");
                ViewBag.Step = 2;
                ViewBag.Error = "Güvenlik sorusu cevabı doğrulanamadı.";
                return View();
            }
            _loginAttempts.Reset(resetAttemptKey);

            await SendResetLinkAsync(user);
            ViewBag.Success = "Bilgiler doğrulandı. Şifre sıfırlama bağlantısı e-posta adresinize gönderildi.";
            return View();
        }

        if (user != null && user.IsActive && string.IsNullOrWhiteSpace(user.SecurityQuestion))
        {
            await SendResetLinkAsync(user);
        }
        ViewBag.Success = "Bilgiler uygunsa şifre sıfırlama bağlantısı e-posta adresinize gönderildi.";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string email, string token)
    {
        if (!await IsResetTokenValid(email, token)) return View("ResetPasswordInvalid");
        ViewBag.Email = email;
        ViewBag.Token = token;
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Email == email.Trim().ToLowerInvariant());
        ViewBag.SecurityQuestion = user?.SecurityQuestion;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string email, string token, string newPassword, string confirmPassword, string? securityAnswer)
    {
        if (!await IsResetTokenValid(email, token)) return View("ResetPasswordInvalid");
        var resetUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Email == email.Trim().ToLowerInvariant());
        if (resetUser != null && !string.IsNullOrWhiteSpace(resetUser.SecurityQuestion) && !VerifySecurityAnswer(securityAnswer ?? string.Empty, resetUser.SecurityAnswer))
        {
            ViewBag.Error = "Güvenlik sorusu cevabı doğrulanamadı.";
            ViewBag.Email = email; ViewBag.Token = token; ViewBag.SecurityQuestion = resetUser.SecurityQuestion;
            return View();
        }
        if ((newPassword?.Length ?? 0) > 256 || (confirmPassword?.Length ?? 0) > 256)
        {
            ViewBag.Error = "Şifre en fazla 256 karakter olabilir.";
            ViewBag.Email = email; ViewBag.Token = token; ViewBag.SecurityQuestion = resetUser?.SecurityQuestion;
            return View();
        }
        if (newPassword != confirmPassword)
        {
            ViewBag.Error = "Şifreler eşleşmiyor.";
            ViewBag.Email = email; ViewBag.Token = token; ViewBag.SecurityQuestion = resetUser?.SecurityQuestion;
            return View();
        }
        if (!_passwords.IsStrongEnough(newPassword))
        {
            ViewBag.Error = "Şifre en az 6 karakter; büyük/küçük harf, rakam ve özel karakter içermelidir.";
            ViewBag.Email = email; ViewBag.Token = token; ViewBag.SecurityQuestion = resetUser?.SecurityQuestion;
            return View();
        }

        var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await _context.Users.FirstAsync(x => x.Email == normalizedEmail);
        var hash = _tokens.HashToken(token);
        var reset = await _context.PasswordResetTokens.FirstAsync(x => x.UserId == user.Id && x.TokenHash == hash && x.UsedAt == null);
        user.Password = _passwords.Hash(newPassword);
        reset.UsedAt = DateTime.Now;
        await _context.SaveChangesAsync();
        await _audit.LogAsync("Şifre Sıfırlandı", "User", user.Id, "Kullanıcı e-posta tokenı ile şifresini sıfırladı.");

        TempData["SuccessMessage"] = "Şifreniz güvenli şekilde güncellendi. Giriş yapabilirsiniz.";
        return RedirectToAction(nameof(Login));
    }

    private async Task<bool> IsResetTokenValid(string email, string token)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token)) return false;
        email = email.Trim().ToLowerInvariant();
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Email == email);
        if (user == null) return false;
        var hash = _tokens.HashToken(token);
        return await _context.PasswordResetTokens.AsNoTracking().AnyAsync(x => x.UserId == user.Id && x.TokenHash == hash && x.UsedAt == null && x.ExpiresAt > DateTime.Now);
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> Settings(string? logAction, DateTime? logDateFrom, DateTime? logDateTo, int logPage = 1)
    {
        var user = await CurrentUserAsync();
        if (user == null) return RedirectToAction(nameof(Login));
        ViewBag.SecurityQuestions = SecurityQuestions;
        ViewBag.ThemePreference = NormalizeThemePreference(user.ThemePreference);

        const int logPageSize = 10;
        logPage = Math.Max(1, logPage);
        var logsQuery = _context.AuditLogs.AsNoTracking().Where(x => x.UserId == user.Id);
        if (logDateFrom.HasValue) logsQuery = logsQuery.Where(x => x.CreatedAt >= logDateFrom.Value.Date);
        if (logDateTo.HasValue) logsQuery = logsQuery.Where(x => x.CreatedAt < logDateTo.Value.Date.AddDays(1));
        var allUserLogs = await logsQuery.OrderByDescending(x => x.CreatedAt).ToListAsync();
        if (!string.IsNullOrWhiteSpace(logAction))
        {
            var value = logAction.Trim();
            allUserLogs = allUserLogs.Where(x => ContainsIgnoreCaseTr(x.Action, value)).ToList();
        }

        var total = allUserLogs.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)logPageSize));
        logPage = Math.Min(logPage, totalPages);

        ViewBag.UserLogs = allUserLogs.Skip((logPage - 1) * logPageSize).Take(logPageSize).ToList();
        ViewBag.LogTotal = total;
        ViewBag.LogPage = logPage;
        ViewBag.LogTotalPages = totalPages;
        ViewBag.LogAction = logAction;
        ViewBag.LogDateFrom = logDateFrom?.ToString("yyyy-MM-dd");
        ViewBag.LogDateTo = logDateTo?.ToString("yyyy-MM-dd");
        return View(user);
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(string username, string email, string currentPassword, string? newPassword, string securityQuestion, string? securityAnswer)
    {
        var user = await CurrentUserAsync();
        if (user == null) return RedirectToAction(nameof(Login));

        username = (username ?? string.Empty).Trim();
        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        securityQuestion = (securityQuestion ?? string.Empty).Trim();
        if (!_passwords.Verify(currentPassword ?? string.Empty, user.Password))
            ViewBag.Error = "Mevcut şifreniz hatalı.";
        else if (username.Length < 3 || username.Length > 80)
            ViewBag.Error = "Kullanıcı adı 3-80 karakter olmalıdır.";
        else if (email.Length > 180 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            ViewBag.Error = "Geçerli bir e-posta adresi girin.";
        else if (await _context.Users.AnyAsync(u => u.Id != user.Id && EF.Functions.Collate(u.Email, "NOCASE") == email) ||
                 (await _context.Users.AsNoTracking().Where(u => u.Id != user.Id).Select(u => u.Username).ToListAsync()).Any(x => EqualsIgnoreCaseTr(x, username)))
            ViewBag.Error = "Kullanıcı adı veya e-posta başka bir hesapta kullanılıyor.";
        else if (!string.IsNullOrWhiteSpace(newPassword) && _passwords.Verify(newPassword, user.Password))
            ViewBag.Error = "Yeni şifreniz mevcut şifrenizle aynı olamaz.";
        else if (!string.IsNullOrWhiteSpace(newPassword) && newPassword.Length > 256)
            ViewBag.Error = "Yeni şifre en fazla 256 karakter olabilir.";
        else if (!string.IsNullOrWhiteSpace(newPassword) && !_passwords.IsStrongEnough(newPassword))
            ViewBag.Error = "Yeni şifre en az 6 karakter; büyük/küçük harf, rakam ve özel karakter içermelidir.";
        else if (!SecurityQuestions.Contains(securityQuestion))
            ViewBag.Error = "Geçerli bir güvenlik sorusu seçin.";
        else if (securityQuestion != user.SecurityQuestion && string.IsNullOrWhiteSpace(securityAnswer))
            ViewBag.Error = "Güvenlik sorusunu değiştiriyorsanız yeni cevabı da girmelisiniz.";
        else if (!string.IsNullOrWhiteSpace(securityAnswer) && (securityAnswer.Trim().Length < 2 || securityAnswer.Trim().Length > 200))
            ViewBag.Error = "Güvenlik sorusu cevabı 2-200 karakter olmalıdır.";

        if (ViewBag.Error != null)
        {
            ViewBag.SecurityQuestions = SecurityQuestions;
            ViewBag.ThemePreference = NormalizeThemePreference(user.ThemePreference);
            ViewBag.UserLogs = await _context.AuditLogs.AsNoTracking().Where(x => x.UserId == user.Id).OrderByDescending(x => x.CreatedAt).Take(15).ToListAsync();
            ViewBag.LogTotal = await _context.AuditLogs.AsNoTracking().CountAsync(x => x.UserId == user.Id);
            return View(user);
        }

        var old = new { user.Username, user.Email };
        user.Username = username;
        user.Email = email;
        user.SecurityQuestion = securityQuestion;
        if (!string.IsNullOrWhiteSpace(securityAnswer)) user.SecurityAnswer = HashSecurityAnswer(securityAnswer);
        if (!string.IsNullOrWhiteSpace(newPassword)) user.Password = _passwords.Hash(newPassword);
        await _context.SaveChangesAsync();
        await _audit.LogAsync("Hesap Güncelleme", "User", user.Id, "Kullanıcı hesap bilgilerini güncelledi.", old, new { user.Username, user.Email, PasswordChanged = !string.IsNullOrWhiteSpace(newPassword) });

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.Username), new(ClaimTypes.Email, user.Email), new(ClaimTypes.Role, user.IsAdmin ? "Admin" : "User"),
            new("session_id", User.FindFirstValue("session_id") ?? string.Empty)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        TempData["SuccessMessage"] = "Hesap bilgileriniz güncellendi.";
        return RedirectToAction(nameof(Settings));
    }



    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTheme(string? themePreference, string? returnUrl = null)
    {
        var user = await CurrentUserAsync();
        if (user == null) return Unauthorized();

        var preference = NormalizeThemePreference(themePreference);
        var oldPreference = NormalizeThemePreference(user.ThemePreference);
        if (!string.Equals(oldPreference, preference, StringComparison.Ordinal))
        {
            user.ThemePreference = preference;
            await _context.SaveChangesAsync();
            await _audit.LogAsync("Tema Değiştirildi", "User", user.Id,
                $"Tema tercihi {ThemeLabel(oldPreference)} → {ThemeLabel(preference)} olarak değiştirildi.",
                new { ThemePreference = oldPreference }, new { ThemePreference = preference });
        }

        if (Request.Headers.Accept.Any(x => x?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true))
            return Json(new { success = true, preference });

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);
        return RedirectToAction(nameof(Settings));
    }

    private static string NormalizeThemePreference(string? value)
        => value is "light" or "dark" ? value : "system";

    private static string ThemeLabel(string value) => value switch
    {
        "light" => "Açık",
        "dark" => "Koyu",
        _ => "Sistem Varsayılanı"
    };

    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");
    private static bool EqualsIgnoreCaseTr(string? left, string? right) => TurkishCulture.CompareInfo.Compare(left ?? string.Empty, right ?? string.Empty, CompareOptions.IgnoreCase) == 0;
    private static bool ContainsIgnoreCaseTr(string? source, string? value) => TurkishCulture.CompareInfo.IndexOf(source ?? string.Empty, value ?? string.Empty, CompareOptions.IgnoreCase) >= 0;

    private string HashSecurityAnswer(string answer) => _passwords.Hash("answer|" + (answer ?? string.Empty).Trim().ToLowerInvariant());

    private bool VerifySecurityAnswer(string? answer, string? storedHash)
        => !string.IsNullOrWhiteSpace(answer) && !string.IsNullOrWhiteSpace(storedHash) &&
           _passwords.Verify("answer|" + answer.Trim().ToLowerInvariant(), storedHash);

    private async Task SendResetLinkAsync(User user)
    {
        var oldTokens = await _context.PasswordResetTokens.Where(x => x.UserId == user.Id && x.UsedAt == null).ToListAsync();
        foreach (var old in oldTokens) old.UsedAt = DateTime.Now;
        var rawToken = _tokens.CreateToken();
        _context.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = _tokens.HashToken(rawToken),
            ExpiresAt = DateTime.Now.AddMinutes(30),
            CreatedAt = DateTime.Now
        });
        await _context.SaveChangesAsync();
        var resetUrl = await BuildPublicResetUrlAsync(user.Email, rawToken);
        if (string.IsNullOrWhiteSpace(resetUrl))
        {
            TempData["WarningMessage"] = "Şifre sıfırlama bağlantısı gönderilemedi. Admin > Sistem Ayarları bölümünde geçerli bir dış erişim adresi tanımlayın.";
            await _audit.LogAsync("Şifre Sıfırlama Linki Oluşturulamadı", "User", user.Id, "Geçerli dış erişim adresi bulunamadığı için şifre sıfırlama e-postası gönderilmedi.");
            return;
        }
        var safeResetUrl = System.Net.WebUtility.HtmlEncode(resetUrl);
        var result = await _email.SendAsync(user.Email, "Ayaz Teknoloji - Şifre Sıfırlama",
            MailTemplateService.Account(
                "Şifrenizi sıfırlayın",
                "Şifre sıfırlama talebiniz için güvenli bağlantı oluşturuldu. Bağlantı 30 dakika geçerlidir.",
                $"<div style=\"text-align:center;margin:18px 0\"><a href=\"{safeResetUrl}\" style=\"display:inline-block;background:#185c72;color:#fff;text-decoration:none;padding:12px 20px;border-radius:10px;font-weight:700\">Şifremi Sıfırla</a></div><p style=\"font-size:13px;color:#64748b;line-height:1.6\">Bu talebi siz oluşturmadıysanız e-postayı yok sayabilirsiniz.</p>",
                "Güvenlik"));
        if (!result.Success) TempData["WarningMessage"] = result.Message;
        await _audit.LogAsync("Şifre Sıfırlama Talebi", "User", user.Id, "Güvenlik doğrulaması sonrası şifre sıfırlama bağlantısı oluşturuldu.");
    }


    private async Task<string?> BuildPublicResetUrlAsync(string email, string token)
    {
        var publicBaseUrl = await _context.SystemSettings.AsNoTracking()
            .Where(x => x.Key == "PublicBaseUrl")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
        publicBaseUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? _configuration["PublicBaseUrl"] : publicBaseUrl;
        publicBaseUrl = publicBaseUrl?.Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(publicBaseUrl) ||
            !Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            uri.IsLoopback ||
            string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            return null;

        return $"{publicBaseUrl}/Account/ResetPassword?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }

    [Authorize]
    public IActionResult AccessDenied() => View();

    private async Task<User?> CurrentUserAsync()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        return await _context.Users.FindAsync(id);
    }
}
