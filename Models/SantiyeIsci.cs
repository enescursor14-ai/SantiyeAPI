namespace SantiyeAPI.Models;

public class SantiyeIsci
{
    public int IsciId { get; set; }
    public Isci? Isci { get; set; }  // ✅ Nullable - null check daha güvenli

    public int SantiyeId { get; set; }
    public Santiye? Santiye { get; set; }  // ✅ Nullable - null check daha güvenli

    // 🎯 2. SENARYO (ŞANTİYEDEN ÇIKARMA) İÇİN GEREKLİ ALANLAR:
    public bool AktifMi { get; set; } = true; // Şantiyede çalışmaya devam ediyor mu?
    public DateTime KatilmaTarihi { get; set; } = DateTime.UtcNow; // Şantiyeye ne zaman girdi?
    public DateTime? AyrilmaTarihi { get; set; } // Şantiyeden ne zaman
    //  çıkarıldı?
    public bool IsDeleted { get; set; } // Yumuşak silme için
    
}