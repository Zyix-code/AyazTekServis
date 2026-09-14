using Microsoft.AspNetCore.DataProtection;

namespace AyazTekServis.Services;

public class SecretProtectionService(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("AyazTekServis.SmtpSecret.v1");

    public string Protect(string value) => _protector.Protect(value);

    public string Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return string.Empty;
        try { return _protector.Unprotect(protectedValue); }
        catch { return string.Empty; }
    }
}
