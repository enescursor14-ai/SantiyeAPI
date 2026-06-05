using Microsoft.EntityFrameworkCore;
using SantiyeAPI.Data;
using SantiyeAPI.Models;
using SantiyeAPI.Helpers;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace SantiyeApp.Services;

public sealed class SiteService
{
    // ── Bağımlılıklar ──────────────────────────────────────────
    private readonly AppDbContext _db;
    private readonly ILogger<SiteService> _logger;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };

    // ── Güvenlik Sabitleri ─────────────────────────────────────
    private const int SAAT_TOLERANS_DK = 30;
    private const int DB_TOLERANS_DK = 5;
    private const string HASH_TUZU = "TR_LISANS_2024_SALT";

    private static readonly string[] _zamanKaynaklari =
    [
        "https://www.google.com",
        "https://www.cloudflare.com",
        "https://www.microsoft.com"
    ];

    public SiteService(AppDbContext db, ILogger<SiteService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    // BÖLÜM 1 — SİBER GÜVENLİK MOTORU
    // ══════════════════════════════════════════════════════════════

    #region İnternet Saati

    private static async Task<bool?> InternetSaatiDogruMu()
    {
        foreach (string kaynak in _zamanKaynaklari)
        {
            try
            {
                var yanit = await _http.GetAsync(kaynak, HttpCompletionOption.ResponseHeadersRead);

                if (!yanit.Headers.TryGetValues("Date", out var tarihler))
                    continue;

                if (!DateTime.TryParse(tarihler.FirstOrDefault(), out DateTime internetUtc))
                    continue;

                double farkDk = Math.Abs((DateTime.UtcNow - internetUtc.ToUniversalTime()).TotalMinutes);
                return farkDk < SAAT_TOLERANS_DK;
            }
            catch { }
        }

        return null;
    }

    #endregion

    #region Donanım Kimliği

    private string CihazKimligiGetir()
    {
        try
        {
#pragma warning disable CA1416
            string mac = WmiOku("Win32_NetworkAdapterConfiguration", "MACAddress");
            string cpu = WmiOku("Win32_Processor", "ProcessorId");
            string anakart = WmiOku("Win32_BaseBoard", "SerialNumber");
#pragma warning restore CA1416

            return Sha256($"{mac}|{cpu}|{anakart}|{HASH_TUZU}");
        }
        catch
        {
            return Sha256(Environment.MachineName + HASH_TUZU);
        }
    }

#pragma warning disable CA1416
    private static string WmiOku(string wmiSinif, string ozellik)
    {
        using var sorgu = new ManagementObjectSearcher($"SELECT {ozellik} FROM {wmiSinif}");

        foreach (ManagementObject nesne in sorgu.Get())
        {
            string? deger = nesne[ozellik]?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(deger)) return deger;
        }

        return "BILINMIYOR";
    }
#pragma warning restore CA1416

    #endregion

    #region DB Tamper Koruması

    public static string DamgaHashiOlustur(DateTime tarih, string donanimKimligi, int jetonSayisi)
    {
        string veri = $"{tarih:yyyyMMddHHmmss}|{donanimKimligi}|{jetonSayisi}|{HASH_TUZU}";
        return Sha256(veri);
    }
    private static bool DamgaGecerliMi(Company company)
    {
        // ✅ Hem hash silinmişse hem de tarih silinmişse anında kapıyı kilitle!
        if (string.IsNullOrEmpty(company.DamgaHash) || !company.SonIslemTarihi.HasValue)
            return false;

        string beklenen = DamgaHashiOlustur(
            company.SonIslemTarihi.Value,
            company.DonanimKimligi ?? string.Empty,
            company.AllowedActiveSiteCount
        );
        return company.DamgaHash == beklenen;
    }
    #endregion

    #region Yardımcı — SHA-256

    private static string Sha256(string metin)
    {
        byte[] bayt = SHA256.HashData(Encoding.UTF8.GetBytes(metin));
        return Convert.ToHexString(bayt).ToLowerInvariant();
    }

    #endregion

    #region Ana Güvenlik Taraması


    // 🚀 Sınıfın en üstündeki değişkenlerin arasına bunu ekle (Yoksa ekle)


    // Sınıfın üstüne ekle
    private static readonly SemaphoreSlim _kurulumKilidi = new(1, 1);

    // ── Yardımcı: dönüş değerleri ──────────────────────────────
    private static (bool, string) Guvenli() => (true, string.Empty);
    private static (bool, string) Engelle(string mesaj) => (false, mesaj);

    // ── Yardımcı: zaman damgası güncelle ──────────────────────
    private static void AktifZamaniGuncelle(Company company, DateTime zaman)
    {
        company.SonIslemTarihi = zaman;
        company.DamgaHash = DamgaHashiOlustur(
            zaman,
            company.DonanimKimligi ?? string.Empty,
            company.AllowedActiveSiteCount
        );
    }

    // ── Yardımcı: kaydet (beklenen hataları logla, yutma) ─────
    private async Task KaydetGuvenlice(int companyId)
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex,
                "Damga kaydedilemedi (DbUpdateException) — companyId={Id}", companyId);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 5)
        {
            _logger.LogWarning(ex,
                "Damga kaydedilemedi (SQLite kilitli) — companyId={Id}", companyId);
        }
    }

    // ══════════════════════════════════════════════════════════
    // ANA GÜVENLİK TARAMASI
    // ══════════════════════════════════════════════════════════
    public async Task<(bool GuvenliMi, string HataMesaji)> GuvenlikTarasiYap(int companyId)
    {
        var company = await _db.Companies.FindAsync(companyId);
        DateTime trSimdi = ZamanMotoru.SimdiTurkiye();
        string guncelKimlik = CihazKimligiGetir();

        // ── Katman 0: İlk Kurulum & Race Condition Zırhı ──────
        if (company is null)
        {
            await _kurulumKilidi.WaitAsync();
            try
            {
                // Çift kontrol: ilk giren oluşturur, bekleyenler hazır olanı bulur
                company = await _db.Companies.FindAsync(companyId);
                if (company is null)
                {
                    company = new Company
                    {
                        Id = companyId,
                        Name = "Bizim Şantiye A.Ş.",
                        AllowedActiveSiteCount = 0,
                        DonanimKimligi = guncelKimlik,
                        SonIslemTarihi = trSimdi
                    };
                    company.DamgaHash = DamgaHashiOlustur(trSimdi, guncelKimlik, company.AllowedActiveSiteCount);

                    _db.Companies.Add(company);
                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                // PK çakışması veya başka geçici hata — kazananın kaydını oku
                _logger.LogWarning(ex, "Kurulum çakışması — companyId={Id}", companyId);
                _db.ChangeTracker.Clear();
                company = await _db.Companies.FindAsync(companyId);
            }
            finally
            {
                _kurulumKilidi.Release();
            }

            // Hâlâ null ise ciddi bir DB sorunu var; Program.cs halleder
            if (company is null) return Guvenli();
        }

        // ── Katman 1: DB Bütünlük Damgası ─────────────────────
        // ── Katman 1: DB Bütünlük Damgası ─────────────────────
        if (!DamgaGecerliMi(company))
        {
            // 🚑 KURTARMA ZIRHI: Eski kayıtlarda henüz mühür yoksa (hash=null), sistemi kilitleme!
            // Hemen yeni formülle ilk mührü bas ve yola devam et (Geçiş senaryosu).
            if (string.IsNullOrEmpty(company.DamgaHash))
            {
                _logger.LogWarning("Eski veritabanı mühürsüz bulundu. İlk mühür basılıyor... — companyId={Id}", companyId);
                AktifZamaniGuncelle(company, trSimdi);
                await KaydetGuvenlice(companyId);
            }
            else
            {
                // Ama mühür VARSA ve eşleşmiyorsa (Gerçek Hacker müdahalesi) o zaman acımadan kilitle!
                _logger.LogCritical(
                    "DB bütünlük hatası — companyId={Id}, hash={Hash}",
                    companyId, company.DamgaHash);

                return Engelle("🛑 VERİTABANI BÜTÜNLÜK HATASI! Lisans kayıtları dışarıdan değiştirilmiş. (Hata Kodu: 0x44)");
            }
        }

        // ── Katman 2: İnternet Saati ───────────────────────────
        bool? internet = await InternetSaatiDogruMu();

        if (internet == false)
            return Engelle("🛑 BİLGİSAYAR SAATİ YANLIŞ! Windows saatini otomatiğe alıp yeniden başlatın.");

        // ── Katman 3: Zaman Hilesi ─────────────────────────────
        bool saatGeriAlindi = company.SonIslemTarihi.HasValue
            && trSimdi < company.SonIslemTarihi.Value.AddMinutes(-DB_TOLERANS_DK);

        if (saatGeriAlindi)
        {
            if (internet == true)
            {
                _logger.LogWarning(
                    "Saat geri alma tespit edildi — companyId={Id}, sonIslem={Son}, simdi={Simdi}",
                    companyId, company.SonIslemTarihi, trSimdi);

                AktifZamaniGuncelle(company, trSimdi);
                try { await _db.SaveChangesAsync(); } catch { /* Katman 5 tekrar dener */ }
            }
            else
            {
                return Engelle("🛑 SİSTEM SAATİ GERİ ALINMIŞ ve İnternet yok. Saati düzeltip internete bağlanın.");
            }
        }

        // ── Katman 4: Donanım Klonlama ─────────────────────────
        if (string.IsNullOrEmpty(company.DonanimKimligi) || company.DonanimKimligi == "BEKLIYOR")
        {
            company.DonanimKimligi = guncelKimlik;
        }
        else if (!string.Equals(company.DonanimKimligi, guncelKimlik, StringComparison.Ordinal))
        {
            _logger.LogCritical(
                "Donanım uyuşmazlığı — companyId={Id}, kayitli={Kayitli}, guncel={Guncel}",
                companyId, company.DonanimKimligi, guncelKimlik);

            return Engelle("🛑 LİSANS İHLALİ! Bu veritabanı başka bilgisayardan kopyalanmış.");
        }

        // ── Katman 5: Zaman Damgasını Güncelle (1 dk toleranslı)
        bool damgaGerekli = !company.SonIslemTarihi.HasValue
            || (trSimdi - company.SonIslemTarihi.Value).TotalMinutes > 1;

        if (damgaGerekli)
        {
            AktifZamaniGuncelle(company, trSimdi);
            await KaydetGuvenlice(companyId);
        }

        return Guvenli();
    }

    #endregion

    // ══════════════════════════════════════════════════════════════
    // BÖLÜM 2 — LİSANS VE FİRMA İŞLEMLERİ
    // ══════════════════════════════════════════════════════════════

    public async Task<(int Jeton, string FirmaAdi, bool GuvenliMi, string Hata)> FirmaDurumGetirAsync(int companyId)
    {
        var (guvenli, hata) = await GuvenlikTarasiYap(companyId);
        if (!guvenli) return (0, string.Empty, false, hata);

        var company = await _db.Companies.FindAsync(companyId);
        if (company is null) return (0, "Bilinmeyen Firma", true, string.Empty);

        return (company.AllowedActiveSiteCount, company.Name, true, string.Empty);
    }

    public async Task<(bool Basarili, string Mesaj)> SantiyeEkleAsync(
        int companyId, string siteName, string siteLocation)
    {
        var (guvenli, hata) = await GuvenlikTarasiYap(companyId);
        if (!guvenli) return (false, hata);

        await using var tx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);
        try
        {
            var company = await _db.Companies
                .Include(c => c.Santiyeler)
                .FirstOrDefaultAsync(c => c.Id == companyId);

            // 🚀 MANUEL OLUŞTURMAYI SİLDİK! Çünkü yukarıdaki GuvenlikTarasiYap zaten oluşturdu!
            if (company is null) return (false, "Firma güvenlik duvarını geçemedi!");

            if (company.AllowedActiveSiteCount <= 0)
                return (false, "Jeton kalmadı. Yeni şantiye açmak için lisans alın.");

            company.AllowedActiveSiteCount--;
            AktifZamaniGuncelle(company, ZamanMotoru.SimdiTurkiye()); // 🚀 YENİ ZIRH: Jeton düştüğü an mührü tazele!

            _db.Set<Santiye>().Add(new Santiye
            {
                Ad = siteName,
                Konum = string.IsNullOrWhiteSpace(siteLocation) ? "Belirtilmemiş" : siteLocation,
                CompanyId = company.Id,
                AktifMi = true,
                LisansBitisTarihi = ZamanMotoru.SimdiTurkiye().AddDays(30)


            });

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return (true, $"Şantiye eklendi. Kalan jeton: {company.AllowedActiveSiteCount}");
        }
        // catch bloğu aynı kalıyor...
        catch
        {
            await tx.RollbackAsync();
            return (false, "Şantiye eklenirken hata oluştu.");
        }
    }

    public async Task<(bool Basarili, string Mesaj)> LisansUzatAsync(int companyId, int santiyeId)
    {
        var (guvenli, hata) = await GuvenlikTarasiYap(companyId);
        if (!guvenli) return (false, hata);

        await using var tx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);
        try
        {
            var company = await _db.Companies.FindAsync(companyId);
            if (company is null || company.AllowedActiveSiteCount <= 0)
                return (false, "Yetersiz jeton!");

            var santiye = await _db.Set<Santiye>()
                .FirstOrDefaultAsync(s => s.Id == santiyeId && s.CompanyId == companyId);

            if (santiye is null) return (false, "Şantiye bulunamadı.");

            company.AllowedActiveSiteCount--;
            AktifZamaniGuncelle(company, ZamanMotoru.SimdiTurkiye()); // 🚀 YENİ ZIRH: Jeton düştüğü an mührü tazele!

            if (santiye.LisansBitisTarihi is null || santiye.LisansBitisTarihi < ZamanMotoru.SimdiTurkiye())
            {
                santiye.LisansBitisTarihi = ZamanMotoru.SimdiTurkiye().AddDays(30);


                santiye.AktifMi = true;
            }
            else
            {
                santiye.LisansBitisTarihi = santiye.LisansBitisTarihi.Value.AddDays(30);


            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return (true, $"Süre 30 gün uzatıldı. Kalan jeton: {company.AllowedActiveSiteCount}");
        }
        catch
        {
            await tx.RollbackAsync();
            return (false, "İşlem sırasında hata oluştu.");
        }
    }

    public async Task<(bool Basarili, string Mesaj)> LisansSatinAlAsync(int companyId, int adet)
    {
        var (guvenli, hata) = await GuvenlikTarasiYap(companyId);
        if (!guvenli) return (false, hata);

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var company = await _db.Companies.FindAsync(companyId);
            if (company is null) return (false, "Firma bulunamadı!");

            company.AllowedActiveSiteCount += adet;
            AktifZamaniGuncelle(company, ZamanMotoru.SimdiTurkiye()); // 🚀 YENİ ZIRH: Jeton arttığı an mührü tazele!

            _db.Set<SatinAlmaGecmisi>().Add(new SatinAlmaGecmisi
            {
                CompanyId = company.Id,
                Tarih = DateTime.UtcNow,
                AlinanJetonSayisi = adet,
                OdenenTutar = adet * 2000
            });

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return (true, $"{adet} adet lisans hesabınıza eklendi!");
        }
        catch
        {
            await tx.RollbackAsync();
            return (false, "Ödeme sırasında hata oluştu.");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // BÖLÜM 3 — JETON ŞİFRE SİSTEMİ
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Baban WhatsApp'tan #F1-Z4 kodunu gönderir.
    /// Sen HTML keygen'a F ve Z'yi girip şifreyi üretirsin, babana gönderirsin.
    /// Baban şifreyi girer → tek kullanımlık → bir daha çalışmaz.
    /// </summary>
    public async Task<(bool Basarili, string Mesaj, int YeniJeton)> OfflineJetonYukleAsync(
        int companyId, string girilenKod)
    {
        // 🚀 1. ÖNCE ZIRHI ÇALIŞTIR! (Firma yoksa bile, şifresiyle güvenlice kendi oluşturur)
        var (guvenli, hata) = await GuvenlikTarasiYap(companyId);
        if (!guvenli) return (false, hata, 0);

        // 2. Artık firmayı gönül rahatlığıyla çekebiliriz, çünkü %100 var ve şifreli!
        var company = await _db.Companies.FindAsync(companyId);
        if (company is null) return (false, "Firma bulunamadı!", 0);

        // ── Formülü hesapla ────────────────────────────────────
        // ── Formülü hesapla (HMAC-SHA256 Kriptografik Zırh) ──
        int toplamSantiye = await _db.Set<Santiye>().CountAsync(s => s.CompanyId == company.Id);
        int siberZirh = company.AllowedActiveSiteCount + toplamSantiye;

        // Tarih uyuşmazlığını engellemek için Türkiye saatini alıyoruz
        DateTime trSimdi = ZamanMotoru.SimdiTurkiye();
        string veri = $"{trSimdi:yyyyMMdd}|{company.Id}|{siberZirh}|{HASH_TUZU}";

        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(HASH_TUZU));
        byte[] hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(veri));

        // Hash'in ilk 4 byte'ını alıp 6 haneli (100000 - 999999) benzersiz koda çeviriyoruz
        long dogruSifre = (Math.Abs(BitConverter.ToInt32(hash, 0)) % 900000) + 100000;

        // ── Şifre eşleşiyor mu? ────────────────────────────────
        if (girilenKod != dogruSifre.ToString())
            return (false, "❌ Geçersiz veya tarihi geçmiş şifre!", 0);

        // ── Daha önce kullanıldı mı? ───────────────────────────
        bool dahaOnceKullanildi = await _db.Set<KullanilanSifre>()
            .AnyAsync(k => k.CompanyId == companyId && k.Sifre == girilenKod);

        if (dahaOnceKullanildi)
            return (false, "❌ Bu şifre daha önce kullanılmış!", 0);

        // ── Transaction: kaydet + jeton ver ───────────────────
        await using var tx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);
        try
        {
            _db.Set<KullanilanSifre>().Add(new KullanilanSifre
            {
                CompanyId = companyId,
                Sifre = girilenKod,
                KullanımTarihi = DateTime.UtcNow
            });

            company.AllowedActiveSiteCount++;
            AktifZamaniGuncelle(company, ZamanMotoru.SimdiTurkiye()); // 🚀 YENİ ZIRH: Jeton eklendiği an mührü tazele!

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return (true, "✅ Şifre onaylandı! 1 Jeton yüklendi.", company.AllowedActiveSiteCount);
        }
        catch
        {
            await tx.RollbackAsync();
            return (false, "İşlem sırasında hata oluştu.", 0);
        }
    }
    /// <summary>
    /// WhatsApp mesajına gömülecek Z kodunu döner.
    /// Bu sayede sen keygen'da F ve Z'yi girip şifreyi üretebilirsin.
    /// </summary>
    public async Task<int> SiberZirhKoduGetirAsync(int companyId)
    {
        var company = await _db.Companies.FindAsync(companyId);
        if (company is null) return 0;

        int toplamSantiye = await _db.Set<Santiye>().CountAsync(s => s.CompanyId == companyId);
        return company.AllowedActiveSiteCount + toplamSantiye;
    }
}