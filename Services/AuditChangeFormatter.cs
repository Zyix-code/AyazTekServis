using System.Globalization;
using System.Text.Json;

namespace AyazTekServis.Services;

public static class AuditChangeFormatter
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TrackingNumber"] = "Takip No",
        ["Brand"] = "Marka",
        ["Model"] = "Model",
        ["ProductType"] = "Cins",
        ["SerialNumber"] = "Seri No",
        ["Sender"] = "Gönderen / Müşteri",
        ["CustomerEmail"] = "Müşteri E-postası",
        ["CustomerPhone"] = "Müşteri Telefonu",
        ["ServiceArrivalDate"] = "Kayıt Tarihi",
        ["ServiceSentDate"] = "Servise Gidiş Tarihi",
        ["SentToService"] = "Gönderilen Servis",
        ["FaultReason"] = "Arıza / Şikayet",
        ["Accessories"] = "Aksesuar",
        ["Notes"] = "Kayıt Notu",
        ["ControlNotes"] = "Kontrol Notu",
        ["Status"] = "Durum",
        ["ServiceResult"] = "Yapılan İşlem / Sonuç",
        ["IsUnderWarranty"] = "Garanti",
        ["HasCharge"] = "Ücret Durumu",
        ["ChargeAmount"] = "Servis Ücreti",
        ["ChargeCurrency"] = "Para Birimi",
        ["ChargeVatMode"] = "KDV Durumu",
        ["DeliveryMethod"] = "Teslim Şekli",
        ["DeliveryDate"] = "Teslim Tarihi",
        ["IsActive"] = "Hesap Durumu",
        ["IsAdmin"] = "Yetki",
        ["Username"] = "Kullanıcı Adı",
        ["Email"] = "E-posta",
        ["PasswordChanged"] = "Şifre"
    };

    public static IReadOnlyList<string> Describe(string? oldJson, string? newJson)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(oldJson) && string.IsNullOrWhiteSpace(newJson)) return result;

        try
        {
            var oldMap = ParseObject(oldJson);
            var newMap = ParseObject(newJson);
            var keys = oldMap.Keys.Union(newMap.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(x => !x.Equals("Parts", StringComparison.OrdinalIgnoreCase))
                .Where(x => !x.Equals("HasCharge", StringComparison.OrdinalIgnoreCase))
                .Where(x => !x.Equals("ChargeAmount", StringComparison.OrdinalIgnoreCase))
                .Where(x => !x.Equals("ChargeCurrency", StringComparison.OrdinalIgnoreCase))
                .Where(x => !x.Equals("ChargeVatMode", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in keys)
            {
                oldMap.TryGetValue(key, out var oldElement);
                newMap.TryGetValue(key, out var newElement);
                var oldValue = oldElement.HasValue ? Display(key, oldElement.Value) : "-";
                var newValue = newElement.HasValue ? Display(key, newElement.Value) : "-";
                if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
                    result.Add($"{Labels.GetValueOrDefault(key, key)}: {oldValue} → {newValue}");
            }

            var oldCharge = ReadCharge(oldMap);
            var newCharge = ReadCharge(newMap);
            if (!string.Equals(oldCharge, newCharge, StringComparison.Ordinal))
                result.Add($"Servis Ücreti: {oldCharge} → {newCharge}");

            var oldParts = ReadParts(oldJson);
            var newParts = ReadParts(newJson);
            if (!string.Equals(oldParts, newParts, StringComparison.Ordinal))
                result.Add($"Parça Bilgileri: {oldParts} → {newParts}");
        }
        catch
        {
        }

        return result;
    }

    public static string DescribeInline(string? oldJson, string? newJson) =>
        string.Join(" | ", Describe(oldJson, newJson));

    private static Dictionary<string, JsonElement?> ParseObject(string? json)
    {
        var map = new Dictionary<string, JsonElement?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return map;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return map;
        foreach (var property in doc.RootElement.EnumerateObject())
            map[property.Name] = property.Value.Clone();
        return map;
    }

    private static string Display(string name, JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "-";
        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            var value = element.GetBoolean();
            if (name.Equals("IsUnderWarranty", StringComparison.OrdinalIgnoreCase)) return value ? "GARANTİLİ" : "GARANTİSİZ";
            if (name.Equals("HasCharge", StringComparison.OrdinalIgnoreCase)) return value ? "ÜCRETLİ" : "ÜCRETSİZ";
            if (name.Equals("IsActive", StringComparison.OrdinalIgnoreCase)) return value ? "AKTİF" : "PASİF";
            if (name.Equals("IsAdmin", StringComparison.OrdinalIgnoreCase)) return value ? "YÖNETİCİ" : "KULLANICI";
            if (name.Equals("PasswordChanged", StringComparison.OrdinalIgnoreCase)) return value ? "DEĞİŞTİRİLDİ" : "DEĞİŞMEDİ";
            return value ? "EVET" : "HAYIR";
        }

        if (element.ValueKind == JsonValueKind.Number)
        {
            if (name.Equals("ChargeAmount", StringComparison.OrdinalIgnoreCase) && element.TryGetDecimal(out var amount))
                return amount.ToString("N2", Tr);
            return element.ToString();
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (string.IsNullOrWhiteSpace(value)) return "-";
            if ((name.EndsWith("Date", StringComparison.OrdinalIgnoreCase) || name.EndsWith("At", StringComparison.OrdinalIgnoreCase)) && DateTime.TryParse(value, out var date))
                return date.ToString("dd.MM.yyyy HH:mm", Tr);
            return value;
        }

        return element.ToString();
    }


    private static string ReadCharge(IReadOnlyDictionary<string, JsonElement?> map)
    {
        var hasCharge = map.TryGetValue("HasCharge", out var chargeElement) && chargeElement.HasValue && chargeElement.Value.ValueKind == JsonValueKind.True;
        if (!hasCharge) return "ÜCRETSİZ";

        var amount = "-";
        if (map.TryGetValue("ChargeAmount", out var amountElement) && amountElement.HasValue && amountElement.Value.TryGetDecimal(out var number))
            amount = number.ToString("N2", Tr);
        var currency = map.TryGetValue("ChargeCurrency", out var currencyElement) && currencyElement.HasValue ? Display("ChargeCurrency", currencyElement.Value) : "TRY";
        if (currency == "TRY") currency = "TL";
        var vat = map.TryGetValue("ChargeVatMode", out var vatElement) && vatElement.HasValue ? Display("ChargeVatMode", vatElement.Value) : "KDV Hariç";
        var vatText = vat.Equals("KDV Hariç", StringComparison.OrdinalIgnoreCase) ? "+ KDV" : "(KDV Dahil)";
        return $"{amount} {currency} {vatText}";
    }

    private static string ReadParts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "YOK";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("Parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
                return "YOK";

            var items = new List<string>();
            foreach (var part in parts.EnumerateArray())
            {
                var name = GetString(part, "PartName");
                var serial = GetString(part, "SerialNumber");
                var type = GetString(part, "PartType");
                var quantity = part.TryGetProperty("Quantity", out var q) ? q.ToString() : "1";
                var label = string.IsNullOrWhiteSpace(name) ? "PARÇA" : name;
                var details = new List<string>();
                if (!string.IsNullOrWhiteSpace(type)) details.Add(type);
                if (!string.IsNullOrWhiteSpace(serial)) details.Add($"SN: {serial}");
                details.Add($"Adet: {quantity}");
                items.Add($"{label} ({string.Join(", ", details)})");
            }
            return items.Count == 0 ? "YOK" : string.Join("; ", items);
        }
        catch
        {
            return "YOK";
        }
    }

    private static string GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return string.Empty;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }
}
