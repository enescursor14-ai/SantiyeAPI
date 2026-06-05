using System;
using System.Globalization;

namespace SantiyeAPI.Helpers;

// 1. Senin mevcut Metin Motorun
public static class StringHelper
{
    private static readonly CultureInfo TrCulture = new("tr-TR");

    /// <summary>Türkçe kurallarına göre baş harfleri büyük yapar (Ad, soyad, açıklama vb.).</summary>
    public static string ToTitleCaseTr(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
        return TrCulture.TextInfo.ToTitleCase(value.Trim().ToLower(TrCulture));
    }
}

// 👇 2. İŞTE YENİ ZAMAN MOTORUNU BURAYA, AYRI BİR CLASS OLARAK EKLİYORUZ 👇
public static class ZamanMotoru
{
    // Sunucu nerede olursa olsun GARANTİ Türkiye saatini verir.
    public static DateTime SimdiTurkiye()
    {
        try 
        {
            // Windows Sunucuları İçin
            TimeZoneInfo turkeyZone = TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, turkeyZone);
        } 
        catch (TimeZoneNotFoundException)
        {
            // Mac veya Linux / Docker Sunucuları İçin
            try
            {
                TimeZoneInfo istanbulZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istanbulZone);
            }
            catch (TimeZoneNotFoundException)
            {
                // 🛡️ Son çare (Balyoz Zırhı): Sistemde hiçbir saat dilimi dosyası yoksa bile çökmez! UTC+3 verir.
                return DateTime.UtcNow.AddHours(3);
            }
        }
    }

    // Puantaj ve Avans tabloları için saati 00:00:00 yapar.
    public static DateTime SadeceTarih(DateTime tarih)
    {
        return tarih.Date;
    }
}