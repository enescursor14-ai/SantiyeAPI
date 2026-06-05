using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting; // RateLimit için eklendi
using SantiyeAPI.Data;
using SantiyeAPI.Helpers;
using SantiyeAPI.Models;
using System.Globalization;

namespace SantiyeAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PuantajController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<PuantajController> _logger;

    public PuantajController(AppDbContext context, ILogger<PuantajController> logger)
    {
        _context = context;
        _logger = logger;
    }

    public class HizliPuantajDto
    {
        public int IsciId { get; set; }
        public int SantiyeId { get; set; }
        public decimal Katsayi { get; set; }
        public string Tarih { get; set; } = string.Empty;
    }

    public class TopluYoklamaRequest
    {
        public int SantiyeId { get; set; }
        public DateTime Tarih { get; set; }
    }

    [HttpPost("TopluYoklama")]
    public async Task<IActionResult> TopluYoklamaKaydet([FromBody] TopluYoklamaRequest request)
    {
        if (request == null || request.SantiyeId <= 0 || request.Tarih == default)
            return BadRequest(new { detail = "Geçersiz şantiye veya tarih verisi!" });


        // 🚀 LİSANS VE AKTİFLİK ZIRHI: Şantiye pasifize edildiyse anında reddet
        // 🚀 LİSANS ZIRHI: Lisans bittiğinde yoklama yapılamaz
        DateTime trSimdi = ZamanMotoru.SimdiTurkiye();
        var santiye = await _context.Santiyeler
            .AsNoTracking()
            .Where(s => s.Id == request.SantiyeId)
            .Select(s => new { s.LisansBitisTarihi })
            .FirstOrDefaultAsync();

        if (santiye?.LisansBitisTarihi != null && santiye.LisansBitisTarihi < trSimdi)
        {
            return BadRequest(new { detail = "🛑 Bu şantiyenin lisansı bitmiştir. Yoklama yapılamaz!" });
        }
        var bugun = ZamanMotoru.SimdiTurkiye().Date;
        var hedefTarih = request.Tarih.Date;

        if (hedefTarih > bugun)
            return BadRequest(new { detail = "Gelecek tarihe yoklama girilemez!" });

        // 🛡️ GÜVENLİ: Null değerleri (özellikle GunlukUcret) garanti altına aldık
        var aktifIsciBilgileri = await _context.SantiyeIsciler
            .AsNoTracking()
            .Where(si => si.SantiyeId == request.SantiyeId && si.AktifMi && si.Isci != null && !si.Isci.IsDeleted)
            .Select(si => new { si.IsciId, GunlukUcret = si.Isci!.GunlukUcret })
            .ToDictionaryAsync(si => si.IsciId, si => si.GunlukUcret);

        if (aktifIsciBilgileri.Count == 0)
            return BadRequest(new { detail = "Bu şantiyeye atanmış aktif işçi bulunamadı." });

        var isciIdleri = aktifIsciBilgileri.Keys.ToList();

        var oGunMuhurluOlanIsciIdleri = await _context.GunlukKayitlar
            .AsNoTracking()
            .Where(g => g.SantiyeId == request.SantiyeId && g.Tarih == hedefTarih && isciIdleri.Contains(g.IsciId) && g.OdendiMi)
            .Select(g => g.IsciId)
            .ToHashSetAsync();

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var mevcutKayitlar = await _context.GunlukKayitlar
                .Where(g => g.SantiyeId == request.SantiyeId && g.Tarih == hedefTarih && isciIdleri.Contains(g.IsciId))
                .ToDictionaryAsync(g => g.IsciId);

            int guncellenen = 0, eklenen = 0, atlanan = 0;
            var eklenecekler = new List<GunlukKayit>();

            foreach (var isciId in isciIdleri)
            {
                // 🛡️ GÜVENLİ: TryGetValue ile Dictionary çarpışmasını (Exception) engelledik
                if (!aktifIsciBilgileri.TryGetValue(isciId, out var gunlukUcret))
                {
                    continue; // Bir sorun varsa atla, sistemi çökertme
                }

                if (oGunMuhurluOlanIsciIdleri.Contains(isciId))
                {
                    atlanan++;
                    continue;
                }

                if (mevcutKayitlar.TryGetValue(isciId, out var mevcutKayit))
                {
                    mevcutKayit.CalismaKatsayisi = 1.0m;
                    mevcutKayit.Yevmiye = gunlukUcret;
                    mevcutKayit.Aciklama = "Tam Gün";
                    guncellenen++;
                }
                else
                {
                    eklenecekler.Add(new GunlukKayit
                    {
                        IsciId = isciId,
                        SantiyeId = request.SantiyeId,
                        Tarih = hedefTarih,
                        CalismaKatsayisi = 1.0m,
                        Yevmiye = gunlukUcret,
                        Aciklama = "Tam Gün",
                        OdendiMi = false,
                        IsDeleted = false
                    });
                    eklenen++;
                }
            }

            if (eklenecekler.Count > 0)
                await _context.GunlukKayitlar.AddRangeAsync(eklenecekler);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                mesaj = $"{eklenen + guncellenen} işçi başarıyla eklendi/güncellendi. (Kapalı hesap nedeniyle atlanan: {atlanan})",
                eklenen,
                guncellenen,
                atlanan,
                toplamIsci = aktifIsciBilgileri.Count
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "TopluYoklama başarısız. SantiyeId: {SantiyeId}, Tarih: {Tarih}", request.SantiyeId, hedefTarih);
            return StatusCode(500, new { detail = "Veritabanı işlemi sırasında hata oluştu. Lütfen tekrar deneyin." });
        }
    }


    [HttpPost("hizli-kayit")]
    public async Task<IActionResult> HizliKayit([FromBody] HizliPuantajDto dto)
    {
        // 🛡️ ZIRH: DTO Validasyonu
        var gecerliKatsayilar = new[] { 0.0m, 0.5m, 1.0m, 1.5m, 2.0m };
        if (dto == null || !gecerliKatsayilar.Contains(dto.Katsayi))
            return BadRequest(new { detail = "Geçersiz katsayı! Sadece 0.0, 0.5, 1.0, 1.5 veya 2.0 seçilebilir." });

        if (!DateTime.TryParseExact(dto.Tarih, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var hamTarih))
            return BadRequest(new { detail = "Tarih formatı hatalı. YYYY-AA-GG formatında olmalıdır." });


        // 🚀 LİSANS VE AKTİFLİK ZIRHI: Şantiye pasifize edildiyse yevmiye girişini engelle
        // 🚀 LİSANS ZIRHI: Lisans bittiğinde yevmiye girişi yapılamaz
        DateTime trSimdiHizli = ZamanMotoru.SimdiTurkiye();
        var santiyeHizli = await _context.Santiyeler
            .AsNoTracking()
            .Where(s => s.Id == dto.SantiyeId)
            .Select(s => new { s.LisansBitisTarihi })
            .FirstOrDefaultAsync();

        if (santiyeHizli?.LisansBitisTarihi != null && santiyeHizli.LisansBitisTarihi < trSimdiHizli)
        {
            return BadRequest(new { detail = "🛑 Bu şantiyenin lisansı bitmiştir. Yevmiye girişi yapılamaz!" });
        }

        DateTime bugun = hamTarih.Date;

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 🚀 SENİOR ZIRHI: Eski yavaş kodu attık. Tüm devasa nesneyi (.ToList) belleğe çekmek yerine, 
            // sadece bize gereken "Özet ve Toplam" bilgilerini SQL'den istiyoruz!

            var isciOzet = await _context.Isciler
                .AsNoTracking()
                .Where(i => i.Id == dto.IsciId)
                .Select(i => new
                {
                    GunlukUcret = i.GunlukUcret,
                    SantiyeKatilma = i.SantiyeIsciler.Where(si => si.SantiyeId == dto.SantiyeId).Select(si => (DateTime?)si.KatilmaTarihi).FirstOrDefault(),
                    SantiyeAyrilma = i.SantiyeIsciler.Where(si => si.SantiyeId == dto.SantiyeId && !si.AktifMi).Select(si => (DateTime?)si.AyrilmaTarihi).FirstOrDefault(),

                    // Şantiyede mühürlenmiş son tarih (Varsa)
                    OGunMuhurluMu = i.GunlukKayitlar.Any(g => g.SantiyeId == dto.SantiyeId && g.Tarih == bugun && g.OdendiMi),

                    // O gün "Diğer" şantiyelerde yazdığı mesai toplamı (Aynı şantiyeyi hariç tutuyoruz)
                    DigerSantiyeKatsayiToplami = i.GunlukKayitlar.Where(g => g.Tarih == bugun && g.SantiyeId != dto.SantiyeId).Sum(g => (decimal?)g.CalismaKatsayisi) ?? 0m,

                    // Geçmişe dönük maaş zammı var mı?
                    GecerliMaas = i.MaasGecmisleri.Where(m => m.BaslangicTarihi <= bugun).OrderByDescending(m => m.BaslangicTarihi).Select(m => (decimal?)m.Yevmiye).FirstOrDefault()
                })
                .FirstOrDefaultAsync();

            if (isciOzet == null) return NotFound(new { detail = "İşçi bulunamadı!" });

            if (isciOzet.SantiyeKatilma == null)
                return BadRequest(new { detail = "🚨 GÜVENLİK İHLALİ: Bu usta bu şantiyeye hiç atanmamış!" });

            if (bugun < isciOzet.SantiyeKatilma.Value.Date)
                return BadRequest(new { detail = $"⚠️ GEÇMİŞ ZAMAN HATASI: Usta bu şantiyeye {isciOzet.SantiyeKatilma.Value:dd.MM.yyyy} tarihinde katıldı." });

            if (isciOzet.SantiyeAyrilma.HasValue && bugun > isciOzet.SantiyeAyrilma.Value.Date)
                return BadRequest(new { detail = "⚠️ GELECEK ZAMAN HATASI: Usta bu şantiyeden çıkarılmış." });

            if (isciOzet.OGunMuhurluMu)
                return BadRequest(new { detail = $"⛔ MÜHÜR HATASI: Seçtiğiniz tarih ({bugun:dd.MM.yyyy}) daha önce hesaba katılıp mühürlenmiş. Üzerinde değişiklik yapılamaz!" });

            if (isciOzet.DigerSantiyeKatsayiToplami + dto.Katsayi > 2.0m)
                return BadRequest(new { detail = $"⚠️ İNSAF ŞEFİM: Bir usta diğer şantiyeler dahil günde en fazla Çift Yevmiye (2.0) alabilir! Şu anki diğer şantiye toplamı: {isciOzet.DigerSantiyeKatsayiToplami}" });

            // Sadece güncellenecek kaydı RAM'e (Tracked) alıyoruz
            var mevcutKayit = await _context.GunlukKayitlar
                .FirstOrDefaultAsync(g => g.IsciId == dto.IsciId && g.SantiyeId == dto.SantiyeId && g.Tarih == bugun);

            if (dto.Katsayi == 0m)
            {
                if (mevcutKayit != null)
                {
                    mevcutKayit.CalismaKatsayisi = 0m;
                    mevcutKayit.Yevmiye = 0m;
                    mevcutKayit.Aciklama = "Gelmedi";
                }
                else
                {
                    await _context.GunlukKayitlar.AddAsync(new GunlukKayit
                    {
                        IsciId = dto.IsciId,
                        SantiyeId = dto.SantiyeId,
                        Tarih = bugun,
                        CalismaKatsayisi = 0m,
                        Yevmiye = 0m,
                        Aciklama = "Gelmedi",
                        OdendiMi = false
                    });
                }
            }
            else
            {
                decimal bazAlinacakUcret = (mevcutKayit != null && mevcutKayit.CalismaKatsayisi > 0)
                    ? (mevcutKayit.Yevmiye / mevcutKayit.CalismaKatsayisi)
                    : (isciOzet.GecerliMaas ?? isciOzet.GunlukUcret);

                string aciklamaMetni = dto.Katsayi switch
                {
                    1.0m => "Tam Gün",
                    0.5m => "Yarım Gün",
                    1.5m => "Tam + Mesai",
                    2.0m => "Çift Yevmiye",
                    _ => "Özel Mesai"
                };

                if (mevcutKayit != null)
                {
                    mevcutKayit.CalismaKatsayisi = dto.Katsayi;
                    mevcutKayit.Yevmiye = bazAlinacakUcret * dto.Katsayi;
                    mevcutKayit.Aciklama = aciklamaMetni;
                }
                else
                {
                    await _context.GunlukKayitlar.AddAsync(new GunlukKayit
                    {
                        IsciId = dto.IsciId,
                        SantiyeId = dto.SantiyeId,
                        Tarih = bugun,
                        CalismaKatsayisi = dto.Katsayi,
                        Yevmiye = bazAlinacakUcret * dto.Katsayi,
                        Aciklama = aciklamaMetni,
                        OdendiMi = false
                    });
                }
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return Ok(new { mesaj = "✅ İşlem Başarılı." });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "HizliKayit başarısız. IsciId: {IsciId}, SantiyeId: {SantiyeId}, Katsayi: {Katsayi}", dto.IsciId, dto.SantiyeId, dto.Katsayi);
            return StatusCode(500, new { detail = "Kayıt sırasında teknik bir sorun oluştu. Lütfen tekrar deneyin." });
        }
    }

    [HttpGet("santiye/{santiyeId}/tarih/{tarih}")]
    public async Task<IActionResult> GetGunlukKayitlar(int santiyeId, string tarih)
    {
        if (santiyeId <= 0) return BadRequest(new { detail = "Geçersiz şantiye." });

        if (!DateTime.TryParseExact(tarih, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime seciliTarih))
        {
            return BadRequest(new { detail = "Şefim, takvimde bir hata var. Lütfen geçerli bir tarih seçin." });
        }

        var kayitlar = await _context.GunlukKayitlar
            .AsNoTracking()
            .Where(g => g.SantiyeId == santiyeId && g.Tarih == seciliTarih.Date)
            .Select(g => new { g.IsciId, g.CalismaKatsayisi })
            .ToListAsync();

        return Ok(kayitlar);
    }
}