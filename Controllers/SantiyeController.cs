using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SantiyeAPI.Data;
using SantiyeAPI.Helpers;
using SantiyeAPI.Models;
using SantiyeApp.Services; // SantiyeIsci ara tablosunu tanımak için şart!

namespace SantiyeAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SantiyeController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly SiteService _siteService; // 🚀 YENİ EKLENDİ: Güvenlik Motoru

    public SantiyeController(AppDbContext context, SiteService siteService)
    {
        _context = context;
        _siteService = siteService;
    }

    // 1. ANA EKRAN: Tüm Şantiyeleri ve "Aktif" işçi sayısını ARA TABLODAN çeker
    [HttpGet]
    public async Task<IActionResult> GetSantiyeler([FromQuery] int firmaId)
    {
        // 🛡️ İŞTE ARKA KAPI KİLİDİ: Adam şantiyeleri görmek isterse önce güvenlikten geçmek ZORUNDA!
        var siberDurum = await _siteService.GuvenlikTarasiYap(1); // Parametre olarak şirket ID'sini veriyorsun, genelde 1 olur.
        if (!siberDurum.GuvenliMi)
        {
            // Eğer saat geri alınmışsa veya kopya DB varsa, veritabanına hiç inme, direkt kov!
            return StatusCode(403, new { detail = siberDurum.HataMesaji });
        }

        // --- GÜVENLİKTEN GEÇTİYSE ESKİ KODLARIN AYNEN ÇALIŞMAYA DEVAM EDER ---
        var santiyeler = await _context.Santiyeler
            .AsNoTracking()
            .Where(s => s.AktifMi == true && s.CompanyId == firmaId)
            .Select(s => new
            {
                s.Id,
                s.Ad,
                s.Konum,
                s.AktifMi,
                s.LisansBitisTarihi,
                IsciSayisi = s.SantiyeIsciler.Count(si => si.AktifMi == true)
            })
            .ToListAsync();

        return Ok(santiyeler);
    }
    // 🗄️ YENİ METOT: Sadece Arşivlenmiş (Kapanmış) Şantiyeleri Getirir
    [HttpGet("arsiv")]
    public async Task<IActionResult> GetArsivSantiyeler()
    {
        var arsivler = await _context.Santiyeler
            .AsNoTracking()
            .Where(s => s.AktifMi == false) // Sadece pasif olanlar
            .Select(s => new
            {
                s.Id,
                s.Ad,
                s.Konum,
                s.AktifMi,
                // Arşivdeki şantiyede tüm geçmiş işçilerin toplam sayısını gösterelim
                IsciSayisi = s.SantiyeIsciler.Count()
            })
            .ToListAsync();

        return Ok(arsivler);
    }



    // 🚀 SENIOR DOKUNUŞU: SadeceAktifler parametresi eklendi!
    // 🚀 SENIOR DOKUNUŞU: SadeceAktifler parametresi ve Global Filtre İptali!
    [HttpGet("{santiyeId}/isciler")]
    public async Task<IActionResult> GetIscilerBySantiye(int santiyeId, [FromQuery] bool sadeceAktifler = true)
    {
        // 1. TEMEL SORGUMUZ (Bekçiler henüz görevde)
        var query = _context.SantiyeIsciler
            .AsNoTracking()
            .Where(si => si.SantiyeId == santiyeId && si.Isci != null)
            .AsQueryable();

        if (sadeceAktifler)
        {
            // 🏢 AKTİF ŞANTİYE MODU: Sadece o an çalışanları getir.
            // Global IsDeleted filtreleri DEVREDE KALIR, kovulanlar otomatik gizlenir.
            query = query.Where(si => si.AktifMi == true);
        }
        else
        {
            // 🗄️ ARŞİV MODU: Geçmiş geçmişte kalmıştır. 
            // BÜTÜN BEKÇİLERİ UYUT! (Şirketten silinenleri de pasif olanları da getir)
            query = query.IgnoreQueryFilters();
        }

        // 2. SENİN O EFSANE DOKUNUŞUN!
        var isciler = await query.Select(si => new
        {
            Id = si.Isci!.Id, // Sadece si.Isci.Id yazmak bazen JSON'da hata verebilir, Id = diye belirttik
            Ad = si.Isci.IsDeleted ? si.Isci!.Ad + " (Şirketten Ayrılmış)" : si.Isci.Ad,
            Soyad = si.Isci.Soyad,
            Meslek = si.Isci.Meslek,
            TcNo = si.Isci.TcNo,
            Telefon = si.Isci.Telefon,
            GunlukUcret = si.Isci.GunlukUcret
        })
        .ToListAsync();

        return Ok(isciler);
    }
    [HttpPut("{santiyeId}/isci-cikar/{isciId}")]
    public async Task<IActionResult> IsciCikar(int santiyeId, int isciId)
    {
        // 🚀 1. PERFORMANS: ExecuteUpdate ile RAM'e hiç çekmeden direkt SQL'e "Vur" emri!
        // Bu sayede SaveChanges beklemeden şimşek hızıyla güncelleriz.
        var etkilenenSatir = await _context.SantiyeIsciler
            .Where(si => si.SantiyeId == santiyeId && si.IsciId == isciId && si.AktifMi)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.AktifMi, false)
                .SetProperty(x => x.AyrilmaTarihi, GetTurkeyTime())); // Helper metodumuzu kullandık

        if (etkilenenSatir == 0)
        {
            return BadRequest("Patron, bu usta ya bu şantiyede değil ya da zaten çoktan çıkışı verilmiş!");
        }

        // 💡 SENIOR NOTU: 
        // Artık 'SantiyeIsciler' tablosunda 'AktifMi' false oldu. 
        // GetSantiyeler metodundaki Count(si => si.AktifMi) sorgusu artık bu adamı saymayacak. 
        // Sayı otomatik olarak "Tak" diye düşecek!

        return Ok(new { Mesaj = "Ustanın şantiye çıkışı yapıldı, sayı güncellendi." });
    }

    // --- ŞANTİYE EKLEME (Bu kısım zaten güzeldi, aynen kalıyor) ---
    public class SantiyeCreateDto
    {
        public string Ad { get; set; } = string.Empty;
        public string Konum { get; set; } = string.Empty;
    }

    [HttpPost]
    public async Task<IActionResult> CreateSantiye([FromBody] SantiyeCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Ad))
            return BadRequest(new { detail = "Şantiye adı boş olamaz!" });

        // Önyüzden FirmaId gelmezse güvenlik gereği otomatik 1 numaralı (Ana Firma) kabul et
        int companyId = 1; 

        // 🚀 İŞTE SENİOR ZIRHI: Şantiyeyi eklerken JETONU DA 1 EKSİLTİR!
        var (basarili, mesaj) = await _siteService.SantiyeEkleAsync(companyId, dto.Ad.Trim(), dto.Konum?.Trim() ?? "");

        if (!basarili)
            return BadRequest(new { detail = mesaj }); 

        return Ok(new { mesaj = mesaj });
    }


    // 🏗️ ŞANTİYEYE İŞÇİ GÖREVLENDİRME (REAKTİVASYONLU ZIRHLI METOT)
    [HttpPost("{santiyeId}/isci-ata/{isciId}")]
    public async Task<IActionResult> IsciAta(int santiyeId, int isciId)
    {
        // 1. ŞANTİYE KONTROLÜ
        var santiye = await _context.Santiyeler.FindAsync(santiyeId);
        if (santiye == null) return NotFound("Şantiye bulunamadı patron!");

        // 2. İŞÇİ KONTROLÜ (Soft delete edilmiş mi bakıyoruz)
        var isci = await _context.Isciler.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == isciId);
        if (isci == null) return NotFound("Sistemde böyle bir usta hiç kayıtlı olmamış!");
        if (isci.IsDeleted == true) return BadRequest($"Hop patron! {isci.Ad} isimli usta şirketten tamamen çıkarılmış. Pasif adamı şantiyeye atayamazsın!");

        // 🚀 TÜRKİYE SAATİ
        // 🚀 SENIOR DOKUNUŞU: Cross-Platform (Windows/macOS/Linux) Saat Dilimi Dedektörü
        string tzId = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
            ? "Turkey Standard Time"
            : "Europe/Istanbul";

        TimeZoneInfo turkeyZone = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        DateTime turkiyeSaati = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, turkeyZone);

        // 3. 🎯 COMPOSITE KEY KORUMASI VE REAKTİVASYON
        var mevcutSantiyeKaydi = await _context.SantiyeIsciler
            .FirstOrDefaultAsync(si => si.SantiyeId == santiyeId && si.IsciId == isciId);

        if (mevcutSantiyeKaydi != null)
        {
            // DURUM A: Kayıt var ve usta hala şantiyede ter döküyor
            if (mevcutSantiyeKaydi.AktifMi)
            {
                return BadRequest("Bu usta zaten bu şantiyede aktif olarak çalışıyor.");
            }
            // DURUM B: Usta eskiden çalışmış, ayrılmış, şimdi GERİ DÖNDÜ! (Reaktivasyon)
            else
            {
                mevcutSantiyeKaydi.AktifMi = true; // Şalteri kaldır, adamı aktif et
                mevcutSantiyeKaydi.KatilmaTarihi = turkiyeSaati; // İşe girişini bugün olarak güncelle
                mevcutSantiyeKaydi.AyrilmaTarihi = null; // Çıkış tarihini temizle (Çünkü artık çalışıyor)

                // NOT: Burada _context.Add DEMİYORUZ! Mevcut kaydı Update ediyoruz, PK patlamıyor.
            }
        }
        else
        {
            // DURUM C: Adam bu şantiyeye hayatında ilk defa ayak basıyor
            var yeniAtama = new SantiyeIsci
            {
                SantiyeId = santiyeId,
                IsciId = isciId,
                KatilmaTarihi = turkiyeSaati,
                AktifMi = true
            };
            await _context.SantiyeIsciler.AddAsync(yeniAtama);
        }
        // 🚀 SENIOR DOKUNUŞU: CONCURRENCY (YARIŞ DURUMU) KORUMASI
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Eğer iki kişi aynı anda (veya çift tıklamayla) kaydetmeye çalışırsa ve 
            // veritabanı "Bu Composite Key zaten var" diye patlarsa, sistemi çökertme!
            // Babana kibarca işlemin zaten yapıldığını söyle.
            return BadRequest("Şefim, bir saniye! Bu usta salise farkıyla zaten bu şantiyeye atandı. Ekranı yenilersen listeyi görebilirsin.");
        }

        return Ok(new { mesaj = $"{isci.Ad} {isci.Soyad}, {santiye.Ad} şantiyesine başarıyla atandı!" });
    }

    private DateTime GetTurkeyTime()
    {
        string tzId = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
            ? "Turkey Standard Time" : "Europe/Istanbul";
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(tzId));
    }


    [HttpPut("{id}/kapat")]
    public async Task<IActionResult> SantiyeKapat(int id)
    {
        // 1. Şantiye kontrolü - sadece gerekli field'ları çekiyoruz
        var durum = await _context.Santiyeler
            .Where(s => s.Id == id)
            .Select(s => new { s.AktifMi, s.Ad })
            .FirstOrDefaultAsync();

        if (durum == null) return NotFound(new { detail = "Şantiye bulunamadı." });
        if (!durum.AktifMi) return BadRequest(new { detail = "Şantiye zaten kapalı." });

        // =========================================================================
        // 🚀 MUHASEBE ZIRHI: AÇIK HESAP KONTROLÜ
        // =========================================================================
        bool acikPuantajVarMi = await _context.GunlukKayitlar
            .AnyAsync(g => g.SantiyeId == id && !g.OdendiMi);
        bool acikAvansVarMi = await _context.Avanslar
            .AnyAsync(a => a.SantiyeId == id && !a.OdendiMi);

        if (acikPuantajVarMi || acikAvansVarMi)
        {
            return BadRequest(new
            {
                detail = $"Hop Patron! '{durum.Ad}' projesinde ödenmemiş hesaplar var. " +
                         $"Şantiyeyi kapatmadan önce 'Hesap Kesim' ekranından tüm hesapları mühürlemelisin!"
            });
        }
        // =========================================================================

        // 🚀 BİZİM SİSTEME ÖZEL ZAMAN MOTORU
        DateTime turkiyeSaati = ZamanMotoru.SimdiTurkiye();

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Şantiyeyi kapat
            await _context.Santiyeler
                .Where(s => s.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.AktifMi, false));

            // Şantiyedeki tüm aktif işçileri çıkar ve ayrılma tarihlerini yaz
            await _context.SantiyeIsciler
                .Where(si => si.SantiyeId == id && si.AktifMi)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.AktifMi, false)
                    .SetProperty(x => x.AyrilmaTarihi, turkiyeSaati));

            await transaction.CommitAsync();

            return Ok(new
            {
                mesaj = $"'{durum.Ad}' şantiyesi kapatıldı, işçiler terhis edildi."
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { detail = "Şantiye kapatılırken hata oluştu: " + ex.Message });
        }
    }

    public class GunlukNotRequest
    {
        public int SantiyeId { get; set; }
        public DateTime Tarih { get; set; }
        public string Metin { get; set; } = string.Empty;
    }

    [HttpGet("GunlukNot")]
    public async Task<IActionResult> GetGunlukNot(int santiyeId, DateTime tarih)
    {
        var not = await _context.SantiyeNotlari
            .FirstOrDefaultAsync(n => n.SantiyeId == santiyeId && n.Tarih.Date == tarih.Date);

        return Ok(new { metin = not?.NotMetni ?? "" });
    }

    [HttpPost("GunlukNot")]
    public async Task<IActionResult> KaydetGunlukNot([FromBody] GunlukNotRequest request)
    {
        var not = await _context.SantiyeNotlari
            .FirstOrDefaultAsync(n => n.SantiyeId == request.SantiyeId && n.Tarih.Date == request.Tarih.Date);

        if (not == null)
        {
            not = new SantiyeNotu
            {
                SantiyeId = request.SantiyeId,
                Tarih = request.Tarih.Date,
                NotMetni = request.Metin
            };
            _context.SantiyeNotlari.Add(not);
        }
        else
        {
            not.NotMetni = request.Metin;
        }

        await _context.SaveChangesAsync();
        return Ok(new { mesaj = "Not başarıyla kaydedildi." });
    }

}