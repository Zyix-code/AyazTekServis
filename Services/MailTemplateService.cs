using System.Net;
using System.Text;
using AyazTekServis.Models;

namespace AyazTekServis.Services;

public static class MailTemplateService
{
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    public static string Layout(string title, string intro, string content, string? badge = null, string accent = "#185c72", string? footerNote = null)
    {
        var safeTitle = H(title);
        var safeIntro = H(intro);
        var badgeHtml = string.IsNullOrWhiteSpace(badge)
            ? string.Empty
            : $"<span style=\"display:inline-block;padding:7px 11px;border-radius:999px;background:{accent}14;color:{accent};font-size:12px;font-weight:700;border:1px solid {accent}33\">{H(badge)}</span>";

        return $"""
<!doctype html>
<html lang="tr">
<body style="margin:0;padding:0;background:#f3f6f9;font-family:Arial,Helvetica,sans-serif;color:#172033">
<table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f3f6f9;padding:28px 12px">
<tr><td align="center">
<table role="presentation" width="620" cellspacing="0" cellpadding="0" style="width:100%;max-width:620px;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid #e3e8ef;box-shadow:0 10px 30px rgba(23,32,51,.07)">
<tr><td align="center" style="padding:20px 24px 18px;background:#ffffff;border-bottom:1px solid #e8edf3"><img src="__AYAZ_LOGO__" alt="Ayaz Teknoloji" width="180" style="display:block;width:180px;max-width:70%;height:auto;margin:0 auto;border:0;outline:none;text-decoration:none"></td></tr>
<tr><td align="center" style="padding:24px 28px 10px">{badgeHtml}<h1 style="font-size:22px;line-height:1.3;margin:13px 0 8px;color:#172033;text-align:center">{safeTitle}</h1><p style="font-size:14px;line-height:1.65;color:#596579;margin:0;text-align:center">{safeIntro}</p></td></tr>
<tr><td style="padding:12px 28px 26px">{content}</td></tr>
<tr><td align="center" style="padding:16px 28px;background:#f8fafc;border-top:1px solid #e8edf3;color:#788497;font-size:11px;line-height:1.55;text-align:center">{H(footerNote ?? "Bu e-posta Ayaz Teknoloji teknik servis sistemi tarafından otomatik olarak gönderilmiştir.")}</td></tr>
</table>
</td></tr></table>
</body></html>
""";
    }

    public static string Account(string title, string intro, string bodyHtml, string? badge = null) =>
        Layout(title, intro, bodyHtml, badge, "#185c72");

    public static string ServiceStatus(ServiceRecord record, string title, string intro, string accent = "#185c72", string? actionText = null)
    {
        var action = string.IsNullOrWhiteSpace(actionText)
            ? string.Empty
            : $"<div style=\"margin-top:18px;padding:14px 16px;border-radius:12px;background:{accent}0f;border:1px solid {accent}2d;color:#334155;font-size:14px;line-height:1.55\">{H(actionText)}</div>";

        var detailRows = new StringBuilder();
        if (string.Equals(record.Status, "Teslime Hazır", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(record.Status, "Tamamlandı", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(record.ServiceResult))
                detailRows.Append($"<tr><td style=\"color:#7b8798\">Yapılan İşlem</td><td>{H(record.ServiceResult)}</td></tr>");
        }
        else if (string.Equals(record.Status, "İade Edildi", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(record.ServiceResult))
                detailRows.Append($"<tr><td style="color:#7b8798">İade Açıklaması</td><td>{H(record.ServiceResult)}</td></tr>");
        }
        else if (string.Equals(record.Status, "Müşteri Onayı Bekliyor", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(record.ServiceResult))
                detailRows.Append($"<tr><td style="color:#7b8798">Onay Beklenen İşlem</td><td>{H(record.ServiceResult)}</td></tr>");
        }

        var content = $"""
<table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="border-collapse:separate;border-spacing:0 9px;font-size:14px">
<tr><td style="color:#7b8798;width:145px">Takip No</td><td><strong>{H(record.TrackingNumber)}</strong></td></tr>
<tr><td style="color:#7b8798">Ürün</td><td><strong>{H(record.Brand)} {H(record.Model)}</strong></td></tr>
<tr><td style="color:#7b8798">Cins</td><td>{H(record.ProductType)}</td></tr>
<tr><td style="color:#7b8798">Seri No</td><td>{H(record.SerialNumber)}</td></tr>
<tr><td style="color:#7b8798">Durum</td><td><strong style="color:{accent}">{H(record.Status)}</strong></td></tr>
<tr><td style="color:#7b8798">Arıza / Şikayet</td><td>{H(record.FaultReason)}</td></tr>
{detailRows}
</table>
{action}
""";
        return Layout(title, intro, content, record.Status, accent);
    }

    public static string IntakeMail(ServiceRecord record)
    {
        var content = $"""
<table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="border-collapse:separate;border-spacing:0 8px;font-size:14px">
<tr><td style="color:#7b8798;width:145px">Takip No</td><td><strong>{H(record.TrackingNumber)}</strong></td></tr>
<tr><td style="color:#7b8798">Gönderen</td><td>{H(record.Sender)}</td></tr>
<tr><td style="color:#7b8798">Ürün</td><td><strong>{H(record.Brand)} {H(record.Model)}</strong></td></tr>
<tr><td style="color:#7b8798">Cins</td><td>{H(record.ProductType)}</td></tr>
<tr><td style="color:#7b8798">Seri No</td><td>{H(record.SerialNumber)}</td></tr>
<tr><td style="color:#7b8798">Gönderilen Servis</td><td>{H(record.SentToService)}</td></tr>
<tr><td style="color:#7b8798">Garanti</td><td>{(record.IsUnderWarranty ? "Garantili" : "Garantisiz")}</td></tr>
<tr><td style="color:#7b8798">Arıza / Şikayet</td><td>{H(record.FaultReason)}</td></tr>
</table>
<div style="margin-top:20px;padding:16px;border:1px solid #dfe6ed;border-radius:12px;background:#f8fafc;font-size:13px;line-height:1.65;color:#475569">
<strong style="display:block;color:#172033;margin-bottom:8px">Servise Teslim ve Kabul Şartları</strong>
• Servis formu geri alınmadan ürün teslim edilemez.<br>
• Zamanında teslim alınmayan ürünlerden sorumluluk kabul edilmeyecektir.<br>
• Arıza tespiti en az 2, en çok 7 iş günüdür. Servise giden ürün için azami servis süresi 20 iş günüdür.<br>
• Müdahale sonucu oluşabilecek bilgi kayıpları veya işletim sistemi problemlerinden şirketimiz sorumlu değildir. Ürünü teslim etmeden önce yedeklerinizi alınız.<br>
• Ürün üzerindeki yazılımların yasal sorumluluğu kullanıcıya aittir. Lisansı olmayan yazılımlar yüklenemez.
</div>
""";
        return Layout("Servis kaydınız oluşturuldu", "Ürününüz servis kayıt sistemimize alındı. Aşağıdaki bilgiler servis kabul formunuzun özetidir.", content, "Kayıt Açıldı", "#185c72", "Bu mesaj servis kabul bilgilendirmesidir. Lütfen takip numaranızı saklayınız.");
    }

    public static byte[] IntakeAttachment(ServiceRecord record)
    {
        var html = IntakeMail(record).Replace("__AYAZ_LOGO__", "");
        return Encoding.UTF8.GetBytes(html);
    }

    public static (string Title, string Intro, string Accent, string Action)? StatusMessage(ServiceRecord record)
    {
        return record.Status switch
        {
            "Kayıt Açıldı" => ("Servis kaydınız oluşturuldu", "Ürününüz servis kayıt sistemimize alındı.", "#185c72", "Takip numaranızı saklayınız. Servis sürecindeki önemli değişikliklerde tekrar bilgilendirileceksiniz."),
            "Serviste" => ("Ürününüz teknik servis sürecine alındı", "Servis kaydınızın durumu güncellendi.", "#2563eb", "Ürününüz teknik servis sürecine alınmıştır. Süreçte önemli bir değişiklik olduğunda tekrar bilgilendirileceksiniz."),
                        "Parça Bekliyor" => ("Servis işlemi parça bekliyor", "Ürününüz için servis süreci devam ediyor.", "#d97706", "Onarım için gerekli parça beklenmektedir. Parça temin edildiğinde işlem devam edecektir."),
            "Müşteri Onayı Bekliyor" => ("Servis işlemi için onayınız bekleniyor", "Cihazınızda uygulanacak işlem için müşteri onayı gereken aşamaya gelindi.", "#d97706", "Yukarıdaki işlem bilgisini kontrol ederek teknik servisimizle iletişime geçip onayınızı iletebilirsiniz."),
            "Teslime Hazır" => ("Ürününüz teslime hazır", "Teknik servis işleminiz tamamlandı. Yapılan işlem bilgisi aşağıda yer almaktadır.", "#15803d", "Ürününüz teslim alınmaya hazırdır. Teslim öncesi teknik servisimizle iletişime geçebilirsiniz."),
            "İade Edildi" => ("Ürününüz iade sürecine alındı", "Servis kaydınız iade olarak sonuçlandırıldı. Varsa iade açıklaması aşağıda yer almaktadır.", "#b45309", "Ürününüzün teslim/iade işlemi için teknik servisimizle iletişime geçebilirsiniz."),
            "Tamamlandı" => ("Ürününüz teslim edildi", "Servis ve teslim işleminiz tamamlandı.", "#15803d", "Ürününüzün teslim işlemi sistemimizde tamamlandı. Bizi tercih ettiğiniz için teşekkür ederiz."),
            "Onarılamadı" => ("Servis işlemi sonuçlandı", "Ürününüz için servis işlemi tamamlandı ancak onarım gerçekleştirilemedi.", "#b91c1c", "Detaylı bilgi ve teslim süreci için teknik servis ile iletişime geçebilirsiniz."),
            _ => null
        };
    }
}
