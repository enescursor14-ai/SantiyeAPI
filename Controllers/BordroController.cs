using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SantiyeAPI.Data;
using SantiyeAPI.DTOs;
using ClosedXML.Excel;
using System.IO;
using SantiyeAPI.Models;
using SantiyeAPI.Helpers;
using SantiyeAPI.Exceptions;

namespace SantiyeAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BordroController : ControllerBase
    {
        private readonly AppDbContext _context;

        public BordroController(AppDbContext context)
        {
            _context = context;
        }

        // 📊 1. BORDRO HESAPLAMA (ZAM PARÇALAYICI ZIRHI EKLENDİ)
        // 📊 1. BORDRO HESAPLAMA (BUHARLAŞAN AVANSLAR ÇÖZÜLDÜ)
        [HttpGet("AylikOzet")]
        public async Task<IActionResult> GetAylikBordro(
     [FromQuery] string? ay,
     [FromQuery] bool tumunuGetir = false,
     CancellationToken cancellationToken = default)
        {
            // ── 1. TARİH SINIRLARINI BELİRLE ──────────────────────────────────────
            DateTime kesimTarihiSiniri = DateTime.MaxValue;
            DateTime ayBasi = DateTime.MinValue;

            if (!string.IsNullOrWhiteSpace(ay) && ay != "tumu")
            {
                if (DateTime.TryParseExact(ay, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var seciliTarih))
                {
                    ayBasi = new DateTime(seciliTarih.Year, seciliTarih.Month, 1);
                    kesimTarihiSiniri = ayBasi.AddMonths(1);
                }
                else
                {
                    return BadRequest("Ay formatı hatalı! Beklenen: YYYY-AA (Örn: 2026-02)");
                }
            }

            // ── 2. VERİTABANINDAN ÇEK ─────────────────────────────────────────────
            var hamPuantajlar = await _context.GunlukKayitlar
                .Where(g => g.Tarih >= ayBasi
                         && g.Tarih < kesimTarihiSiniri
                         && (!g.OdendiMi || tumunuGetir))
                .Select(g => new
                {
                    g.IsciId,
                    g.Tarih,
                    g.Yevmiye,
                    g.CalismaKatsayisi,
                    SantiyeId = g.SantiyeId,
                    g.OdendiMi,
                    SantiyeAd = g.Santiye != null ? g.Santiye.Ad : "Belirtilmemiş"
                })
                .ToListAsync(cancellationToken);

            var hamAvanslar = await _context.Avanslar
                .Where(a => a.Tarih >= ayBasi
                         && a.Tarih < kesimTarihiSiniri
                         && (!a.OdendiMi || tumunuGetir))
                .Select(a => new
                {
                    a.Id,
                    a.IsciId,
                    a.Tarih,
                    a.Tutar,
                    SantiyeId = a.SantiyeId ?? 0,
                    a.OdendiMi
                })
                .ToListAsync(cancellationToken);

            // ── 3. HIZLI ERİŞİM İÇİN SÖZLÜĞE AL ──────────────────────────────────
            var puantajIndex = hamPuantajlar
                .GroupBy(p => p.IsciId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var avansIndex = hamAvanslar
                .GroupBy(a => a.IsciId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var oAyIslemGorenIsciIdleri = puantajIndex.Keys.Union(avansIndex.Keys).ToHashSet();

            var isciler = await _context.Isciler
                .IgnoreQueryFilters()
                .Where(i => oAyIslemGorenIsciIdleri.Contains(i.Id))
                .OrderBy(i => i.Ad).ThenBy(i => i.Soyad)
                .Select(i => new
                {
                    i.Id,
                    i.Ad,
                    i.Soyad,
                    i.Meslek,
                    i.GunlukUcret,
                    i.IsDeleted,
                    AktifOlduguSantiyeSayisi = i.SantiyeIsciler.Count(si => si.AktifMi),
                    AktifSantiyeler = i.SantiyeIsciler
                        .Where(si => si.AktifMi)
                        .Select(si => new
                        {
                            si.SantiyeId,
                            SantiyeAd = si.Santiye != null ? si.Santiye.Ad : ""
                        }).ToList()
                })
                .ToListAsync(cancellationToken);

            // ── 4. BORDRO LİSTESİNİ OLUŞTUR ───────────────────────────────────────
            var bordroListesi = new List<BordroOzetDto>(isciler.Count);

            foreach (var isci in isciler)
            {
                var isciPuantajlari = puantajIndex.TryGetValue(isci.Id, out var pl) ? pl : new();
                var isciAvanslari = avansIndex.TryGetValue(isci.Id, out var al) ? al : new();

                string temizAdSoyad = $"{isci.Ad.Trim()} {isci.Soyad.Trim()}".Trim();
                string pasifEki = isci.IsDeleted ? " (Pasif)" : "";

                // Avansları şantiyeye göre grupla (hızlı erişim için)
                var avansPerSantiye = isciAvanslari
                    .GroupBy(a => a.SantiyeId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // ── 4a. YEVMİYE GRUPLARINI OLUŞTUR (Mühür durumuna göre ayrı gruplar) ──
                // Aynı yevmiyeli ama biri ödenmiş biri açık kayıtlar AYRI satırlara düşer.
                var hamGruplar = isciPuantajlari
                    .Where(x => x.CalismaKatsayisi > 0)
                    .GroupBy(x => new
                    {
                        Yevmiye = Math.Round((decimal)(x.Yevmiye / x.CalismaKatsayisi), 2),
                        IsMuhurlu = x.OdendiMi
                    })
                    .Select(grup => new
                    {
                        YevmiyeKey = grup.Key.Yevmiye,
                        IsMuhurlu = grup.Key.IsMuhurlu,
                        Kayitlar = grup.ToList(),
                        DonemBaslangic = grup.Min(x => x.Tarih),
                        DonemBitis = grup.Max(x => x.Tarih)
                    })
                    .OrderBy(g => g.DonemBaslangic)
                    .ToList();

                // Her grubun dönem sınırlarını belirle
                // (ilk grubun başı DateTime.MinValue → "tüm geçmiş dahil" anlamı)
                var yevmiyeGruplari = hamGruplar.Select((g, idx) => new
                {
                    g.YevmiyeKey,
                    g.IsMuhurlu,
                    g.Kayitlar,
                    DonemBaslangic = idx == 0
                        ? DateTime.MinValue
                        : g.DonemBaslangic,
                    DonemBitis = idx < hamGruplar.Count - 1
                        ? hamGruplar[idx + 1].DonemBaslangic.AddDays(-1)
                        : (kesimTarihiSiniri == DateTime.MaxValue ? DateTime.MaxValue : kesimTarihiSiniri.AddDays(-1))
                }).ToList();

                // ── 4b. SADECE AVANS OLAN İŞÇİ (Hiç çalışmamış, sadece avans almış) ──
                if (yevmiyeGruplari.Count == 0)
                {
                    if (isciAvanslari.Count == 0) continue; // Ne puantajı ne avansi var, atla

                    var sadeceAvansDetaylari = avansPerSantiye.Select(ap =>
                    {
                        var gosterilecekAvanslar = ap.Value;
                        var acikAvanslar = gosterilecekAvanslar.Where(a => !a.OdendiMi).ToList();
                        decimal odenmemisAvans = acikAvanslar.Sum(a => (decimal)a.Tutar);

                        return new SantiyeGunDetay
                        {
                            SantiyeId = ap.Key,
                            SantiyeAd = isci.AktifSantiyeler.FirstOrDefault(s => s.SantiyeId == ap.Key)?.SantiyeAd ?? "Belirtilmemiş",
                            Hakedis = 0m,
                            AldigiAvans = gosterilecekAvanslar.Sum(a => (decimal)a.Tutar),
                            OdenmemisHakedis = 0m,
                            OdenmemisAvans = odenmemisAvans,
                            Bakiye = -odenmemisAvans,
                            TamGun = 0,
                            YarimGun = 0,
                            MesailiGun = 0,
                            CiftYevmiyeGun = 0,
                            GelmediGun = 0
                        };
                    }).ToList();

                    bordroListesi.Add(new BordroOzetDto
                    {
                        Id = isci.Id,
                        AdSoyad = temizAdSoyad + pasifEki,
                        Meslek = isci.Meslek,
                        SantiyeAd = string.Join(", ", sadeceAvansDetaylari.Select(s => s.SantiyeAd)),
                        Yevmiye = isci.GunlukUcret,
                        IsMuhurlu = false,
                        Hakedis = 0,
                        AldigiAvans = sadeceAvansDetaylari.Sum(d => d.AldigiAvans),
                        CalistigiSantiyeSayisi = isci.AktifOlduguSantiyeSayisi,
                        SantiyeDetaylari = sadeceAvansDetaylari,
                        DonemBaslangic = "", // Çalışmadığı için tarih yok
                        DonemBitis = ""
                    });
                    continue;
                }

                // ── 4c. NORMAL AKIŞ: YEVMİYE GRUPLARI DÖNGÜSÜ ────────────────────
                var kullanilmisAvansIdleri = new HashSet<int>();

                for (int i = 0; i < yevmiyeGruplari.Count; i++)
                {
                    var yevmiyeGrubu = yevmiyeGruplari[i];
                    bool sonSatirMi = i == yevmiyeGruplari.Count - 1;

                    var santiyeGruplari = yevmiyeGrubu.Kayitlar.GroupBy(x => new { x.SantiyeId, x.SantiyeAd }).ToList();
                    var benzersizSantiyeler = santiyeGruplari
                        .Select(x => x.Key.SantiyeAd)
                        .Where(ad => !string.IsNullOrWhiteSpace(ad))
                        .Distinct()
                        .ToList();

                    var detaylar = santiyeGruplari.Select(sg =>
                    {
                        var gosterilecekYevmiyeler = sg.ToList();
                        var santiyeAvanslari = avansPerSantiye.TryGetValue(sg.Key.SantiyeId, out var sa) ? sa : new();

                        // Bu dönemin tarih aralığına giren VE mühür durumu eşleşen avansları al
                        var gosterilecekAvanslar = santiyeAvanslari
                            .Where(a =>
                                a.Tarih.Date >= yevmiyeGrubu.DonemBaslangic.Date &&
                                a.Tarih.Date <= yevmiyeGrubu.DonemBitis.Date &&
                                a.OdendiMi == yevmiyeGrubu.IsMuhurlu)
                            .ToList();

                        // Kullanılan avansları havuza ekle (tekrar kullanılmasın)
                        foreach (var a in gosterilecekAvanslar)
                            kullanilmisAvansIdleri.Add(a.Id);

                        var acikYevmiyeler = gosterilecekYevmiyeler.Where(x => !x.OdendiMi).ToList();
                        var acikAvanslar = gosterilecekAvanslar.Where(a => !a.OdendiMi).ToList();

                        decimal siteOdenmemisHakedis = acikYevmiyeler.Sum(x => (decimal)x.Yevmiye);
                        decimal siteOdenmemisAvans = acikAvanslar.Sum(a => (decimal)a.Tutar);

                        return new SantiyeGunDetay
                        {
                            SantiyeId = sg.Key.SantiyeId,
                            SantiyeAd = string.IsNullOrWhiteSpace(sg.Key.SantiyeAd) ? "Belirtilmemiş" : sg.Key.SantiyeAd,
                            Hakedis = gosterilecekYevmiyeler.Sum(x => (decimal)x.Yevmiye),
                            AldigiAvans = gosterilecekAvanslar.Sum(a => (decimal)a.Tutar),
                            OdenmemisHakedis = siteOdenmemisHakedis,
                            OdenmemisAvans = siteOdenmemisAvans,
                            Bakiye = siteOdenmemisHakedis - siteOdenmemisAvans,
                            TamGun = gosterilecekYevmiyeler.Count(x => x.CalismaKatsayisi == 1m),
                            YarimGun = gosterilecekYevmiyeler.Count(x => x.CalismaKatsayisi == 0.5m),
                            MesailiGun = gosterilecekYevmiyeler.Count(x => x.CalismaKatsayisi == 1.5m),
                            CiftYevmiyeGun = gosterilecekYevmiyeler.Count(x => x.CalismaKatsayisi == 2.0m),
                            GelmediGun = sonSatirMi
                                ? isciPuantajlari.Count(p => p.SantiyeId == sg.Key.SantiyeId && p.CalismaKatsayisi == 0m)
                                : 0
                        };
                    }).ToList();

                    // Son satır zırhı: Tarih aralığına sığmayan (kaçak) avansları son gruba yamala
                    if (sonSatirMi)
                    {
                        foreach (var (sId, avList) in avansPerSantiye)
                        {
                            var kullanilmayanAvanslar = avList
                                .Where(a => !kullanilmisAvansIdleri.Contains(a.Id))
                                .ToList();

                            if (kullanilmayanAvanslar.Count == 0) continue;

                            var mevcutSantiye = detaylar.FirstOrDefault(d => d.SantiyeId == sId);
                            if (mevcutSantiye != null)
                            {
                                mevcutSantiye.AldigiAvans += kullanilmayanAvanslar.Sum(a => (decimal)a.Tutar);
                                mevcutSantiye.OdenmemisAvans += kullanilmayanAvanslar.Where(a => !a.OdendiMi).Sum(a => (decimal)a.Tutar);
                                mevcutSantiye.Bakiye -= kullanilmayanAvanslar.Where(a => !a.OdendiMi).Sum(a => (decimal)a.Tutar);
                            }
                            else
                            {
                                detaylar.Add(new SantiyeGunDetay
                                {
                                    SantiyeId = sId,
                                    SantiyeAd = isci.AktifSantiyeler.FirstOrDefault(s => s.SantiyeId == sId)?.SantiyeAd ?? "Belirtilmemiş",
                                    Hakedis = 0m,
                                    AldigiAvans = kullanilmayanAvanslar.Sum(a => (decimal)a.Tutar),
                                    OdenmemisHakedis = 0m,
                                    OdenmemisAvans = kullanilmayanAvanslar.Where(a => !a.OdendiMi).Sum(a => (decimal)a.Tutar),
                                    Bakiye = -kullanilmayanAvanslar.Where(a => !a.OdendiMi).Sum(a => (decimal)a.Tutar)
                                });
                            }
                        }
                    }

                    // Tarihleri doğrudan kayıtlardan al (güvenli: Kayitlar bu noktada her zaman dolu)
                    bordroListesi.Add(new BordroOzetDto
                    {
                        Id = isci.Id,
                        AdSoyad = temizAdSoyad + pasifEki,
                        Meslek = isci.Meslek,
                        SantiyeAd = benzersizSantiyeler.Count > 0 ? string.Join(", ", benzersizSantiyeler) : "Genel Merkez",
                        Yevmiye = yevmiyeGrubu.YevmiyeKey,
                        IsMuhurlu = yevmiyeGrubu.IsMuhurlu,
                        TamGun = detaylar.Sum(d => d.TamGun),
                        YarimGun = detaylar.Sum(d => d.YarimGun),
                        MesailiGun = detaylar.Sum(d => d.MesailiGun),
                        CiftYevmiyeGun = detaylar.Sum(d => d.CiftYevmiyeGun),
                        GelmediGun = detaylar.Sum(d => d.GelmediGun),
                        Hakedis = detaylar.Sum(d => d.Hakedis),
                        AldigiAvans = detaylar.Sum(d => d.AldigiAvans),
                        CalistigiSantiyeSayisi = isci.AktifOlduguSantiyeSayisi,
                        SantiyeDetaylari = detaylar,
                        // Grubun gerçek çalışma tarihlerini rozet için gönder
                        DonemBaslangic = yevmiyeGrubu.Kayitlar.Any()
                            ? yevmiyeGrubu.Kayitlar.Min(x => x.Tarih).ToString("yyyy-MM-dd")
                            : "",
                        DonemBitis = yevmiyeGrubu.Kayitlar.Any()
                            ? yevmiyeGrubu.Kayitlar.Max(x => x.Tarih).ToString("yyyy-MM-dd")
                            : ""
                    });
                }
            }

            return Ok(bordroListesi);
        }


        // 💰 HESAP KAPATMA VE MÜHÜRLEME (Kısım Kısım Mühürleme Zırhı Eklendi)
        [HttpPost("HesapKapat")]
        public async Task<IActionResult> HesapKapat([FromBody] HesapKesimDto dto)
        {
            // 🛡️ ESKİ: var isci = await _context.Isciler.FindAsync(dto.IsciId);
            var isci = await _context.Isciler.AsNoTracking().FirstOrDefaultAsync(i => i.Id == dto.IsciId);
            if (isci == null) return NotFound(new { mesaj = "Ödeme yapılacak usta bulunamadı!" });
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var patron = await _context.Patronlar.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.PatronId);
            if (patron is not { KasaId: int guvenliKasaId }) return BadRequest("Patronun cüzdanı (kasası) tanımlı değil!");

            // 🚀 EĞER EKRANDAN (Kısım 1 / Kısım 2) GİBİ SPESİFİK TARİH GELDİYSE ONU KULLAN

            // 🛡️ ZIRH 1: KIYAMET GÜNÜ BEKÇİSİ (GUARD CLAUSE)
            // Eğer ne spesifik bir bitiş tarihi var, ne de geçerli bir ay var ise (Tümü seçilmişse) işlemi durdur!
            if (string.IsNullOrWhiteSpace(dto.DonemBitis) && (string.IsNullOrWhiteSpace(dto.Ay) || dto.Ay == "tumu"))
            {
                return BadRequest(new { mesaj = "Hop Patron! Hesap mühürlemek için mutlaka belirli bir AY veya BİTİŞ TARİHİ seçmelisin. Sınırsız (Tüm Zamanlar) mühürleme yapılamaz!" });
            }

            DateTime baslangic = DateTime.MinValue;
            DateTime bitis = DateTime.MaxValue;

            if (!string.IsNullOrWhiteSpace(dto.DonemBaslangic) && DateTime.TryParse(dto.DonemBaslangic, out var db))
                baslangic = db.Date;

            if (!string.IsNullOrWhiteSpace(dto.DonemBitis) && DateTime.TryParse(dto.DonemBitis, out var dbt))
            {
                // 🛡️ ZIRH 2: Overflow (Taşma) Koruması! Çok uçuk bir tarih girilip AddDays(1) ile patlamasını engeller.
                if (dbt.Date >= DateTime.MaxValue.Date.AddDays(-2))
                    return BadRequest(new { mesaj = "Girdiğiniz bitiş tarihi çok büyük!" });

                bitis = dbt.Date.AddDays(1); // Günü kapsamak için +1 gün
            }
            else if (string.IsNullOrWhiteSpace(dto.DonemBaslangic) && !string.IsNullOrWhiteSpace(dto.Ay) && dto.Ay != "tumu")
            {
                // Ekrandan parça gelmediyse, normal ay sınırını kullan
                if (DateTime.TryParseExact(dto.Ay, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var seciliTarih))
                    bitis = new DateTime(seciliTarih.Year, seciliTarih.Month, 1).AddMonths(1);
            }

            var acikPuantajlar = await _context.GunlukKayitlar
                .Where(g => g.IsciId == dto.IsciId && g.SantiyeId == dto.SantiyeId && !g.OdendiMi && g.Tarih >= baslangic && g.Tarih < bitis)
                .Select(g => new { g.Tarih, g.Yevmiye })
                .ToListAsync();

            var acikAvanslar = await _context.Avanslar
                .Where(a => a.IsciId == dto.IsciId && a.SantiyeId == dto.SantiyeId && !a.OdendiMi && a.Tarih >= baslangic && a.Tarih < bitis)
                .Select(a => new { a.Tarih, a.Tutar })
                .ToListAsync();

            if (!acikPuantajlar.Any() && !acikAvanslar.Any())
                return BadRequest("Bu döneme ait mühürlenecek açık bir işlem bulunmuyor.");

            var toplamHakedis = acikPuantajlar.Sum(g => (decimal?)g.Yevmiye) ?? 0;
            var toplamAvans = acikAvanslar.Sum(a => (decimal?)a.Tutar) ?? 0;
            var netOdenen = toplamHakedis - toplamAvans;

            // YENİ — Frontend'e "borçlu" sinyali gönder, oradan BorcDevret'e yönlendir
            if (netOdenen < 0)
                return BadRequest(new
                {
                    kod = "BORC_VAR",
                    mesaj = "Bu ustanın avans borcu var.",
                    borcTutari = Math.Abs(netOdenen)
                });

            var tumTarihler = acikPuantajlar.Select(g => g.Tarih).Union(acikAvanslar.Select(a => a.Tarih)).ToList();
            DateTime gercekBas = tumTarihler.Min();
            DateTime gercekBit = tumTarihler.Max();
            string donemAciklama = gercekBas.Date == gercekBit.Date ? $"{gercekBas:dd MMM yyyy}" : $"{gercekBas:dd MMM} - {gercekBit:dd MMM yyyy}";

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await _context.GunlukKayitlar
                    .Where(g => g.IsciId == dto.IsciId && g.SantiyeId == dto.SantiyeId && !g.OdendiMi && g.Tarih >= baslangic && g.Tarih < bitis)
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.OdendiMi, true));

                await _context.Avanslar
                    .Where(a => a.IsciId == dto.IsciId && a.SantiyeId == dto.SantiyeId && !a.OdendiMi && a.Tarih >= baslangic && a.Tarih < bitis)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.OdendiMi, true));

                if (netOdenen > 0)
                {
                    var yazilacakSantiyeId = patron.SorumluOlduguSantiyeId ?? dto.SantiyeId;
                    var gercekZaman = ZamanMotoru.SimdiTurkiye();

                    var hareket = new KasaHareketi
                    {
                        KasaId = guvenliKasaId,
                        PatronId = patron.Id,
                        SantiyeId = yazilacakSantiyeId,
                        Tutar = netOdenen,
                        Yon = KasaIslemYonu.Cikis,
                        HareketTipi = KasaHareketTipi.MaasOdemesi,
                        IslemTarihi = gercekZaman,
                        Aciklama = $"{isci.Ad} {isci.Soyad} | {donemAciklama} Dönemi Hakediş Ödemesi"
                    };
                    await _context.KasaHareketleri.AddAsync(hareket);

                    var maasOdemesi = new MaasOdemesi
                    {
                        IsciId = dto.IsciId,
                        KasaId = guvenliKasaId,
                        SantiyeId = yazilacakSantiyeId,
                        Tutar = netOdenen,
                        IslemTarihi = gercekZaman,
                        Aciklama = $"{isci.Ad} {isci.Soyad} | {donemAciklama} Dönemi Hakediş Ödemesi",
                        IsDeleted = false
                    };
                    await _context.MaasOdemeleri.AddAsync(maasOdemesi);
                    await _context.SaveChangesAsync();
                }
                await transaction.CommitAsync();

                if (netOdenen == 0) return Ok(new { mesaj = $"{isci.Ad} adlı ustanın bu döneme kadarki hesabı mühürlendi." });
                else return Ok(new { mesaj = $"{patron.Ad} kasasından {netOdenen:C2} ödeme yapıldı ve açık hesap kapatıldı." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, "Finansal hata: " + ex.Message);
            }
        }

        // 🩻 2. İŞÇİ RÖNTGENİ
        // 🩻 2. İŞÇİ RÖNTGENİ (ŞANTİYE VE AY FİLTRELİ)
        // 🩻 2. İŞÇİ RÖNTGENİ (SADECE SEÇİLİ AYIN MATEMATİĞİ - ROKET VERSİYON)
        [HttpGet("IsciDetay")]
        public async Task<IActionResult> GetIsciRontgen([FromQuery] int isciId, [FromQuery] string ay, [FromQuery] int? santiyeId)
        {
            bool tumZamanlarMi = string.Equals(ay, "tumu", StringComparison.OrdinalIgnoreCase);
            DateTime ayBasi = DateTime.MinValue;
            DateTime aySonu = DateTime.MaxValue;

            if (!tumZamanlarMi)
            {
                if (string.IsNullOrWhiteSpace(ay) || !DateTime.TryParseExact(ay, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var seciliTarih))
                    return BadRequest("Ay formatı hatalı. Beklenen format: 2026-02 veya tumu");

                ayBasi = new DateTime(seciliTarih.Year, seciliTarih.Month, 1);
                aySonu = ayBasi.AddMonths(1);
            }

            var isci = await _context.Isciler.IgnoreQueryFilters().Where(i => i.Id == isciId).Select(i => new { i.Id, i.Ad, i.Soyad, i.Meslek }).FirstOrDefaultAsync();
            if (isci == null) return NotFound("İşçi bulunamadı.");

            // 🚀 PERFORMANS ZIRHI: Artık geçmiş aylardaki ödenmemişleri zorla çekmiyoruz!
            // Veritabanına sadece "Bana seçili ayın aralığını getir" diyoruz. Sorgu inanılmaz hızlandı.
            var gunlukTask = _context.GunlukKayitlar
                .AsNoTracking()
                .Where(g => g.IsciId == isciId)
                .Where(g => !santiyeId.HasValue || g.SantiyeId == santiyeId.Value)
                .Where(g => tumZamanlarMi || (g.Tarih >= ayBasi && g.Tarih < aySonu)) // 👈 SADECE SEÇİLİ AY
                .OrderByDescending(g => g.Tarih)
                .Select(g => new { g.Tarih, g.CalismaKatsayisi, g.Aciklama, g.Yevmiye, g.OdendiMi, SantiyeAd = g.Santiye != null ? g.Santiye.Ad : "Bilinmiyor" })
                .ToListAsync();

            var avansTask = _context.Avanslar
                .AsNoTracking()
                .Where(a => a.IsciId == isciId)
                .Where(a => !santiyeId.HasValue || a.SantiyeId == santiyeId.Value)
                .Where(a => tumZamanlarMi || (a.Tarih >= ayBasi && a.Tarih < aySonu)) // 👈 SADECE SEÇİLİ AY
                .OrderByDescending(a => a.Tarih)
                .Select(a => new { a.Tarih, a.Tutar, a.OdemeTuru, a.Aciklama, a.OdendiMi, SantiyeAd = a.Santiye != null ? a.Santiye.Ad : "Bilinmiyor" })
                .ToListAsync();

            await Task.WhenAll(gunlukTask, avansTask);
            var gunlukVeriler = gunlukTask.Result;
            var avansVeriler = avansTask.Result;

            // 🚀 MATEMATİK: Sadece veritabanından süzülüp gelen o aya ait veriler toplanıyor!
            var toplamHakedis = gunlukVeriler.Where(g => !g.OdendiMi).Sum(g => (decimal)g.Yevmiye);
            var toplamAvans = avansVeriler.Where(a => !a.OdendiMi).Sum(a => (decimal)a.Tutar);

            return Ok(new
            {
                adSoyad = $"{isci.Ad} {isci.Soyad}",
                meslek = isci.Meslek,
                toplamHakedis, // Tertemiz sadece o ay
                toplamAvans,   // Tertemiz sadece o ay
                kalanAlacak = toplamHakedis - toplamAvans,

                // RAM'de ekstra filtrelemeye gerek kalmadı çünkü zaten SQL'den filtreli çektik!
                puantajGecmisi = gunlukVeriler.Select(g => new { tarih = g.Tarih.ToString("yyyy-MM-dd"), santiyeAd = g.SantiyeAd, katsayi = g.CalismaKatsayisi, aciklama = g.Aciklama, gunlukKazanc = g.Yevmiye, odendiMi = g.OdendiMi }),
                avansGecmisi = avansVeriler.Select(a => new { tarih = a.Tarih.ToString("yyyy-MM-dd"), santiyeAd = a.SantiyeAd, tutar = a.Tutar, odemeTuru = a.OdemeTuru, aciklama = a.Aciklama, odendiMi = a.OdendiMi })
            });
        }

        // 🚀 ZIRH: Parametrelere 'tumunuGetir = false' eklendi!
        // 🚀 ZIRH: Excel için tumunuGetir parametremiz yerinde duruyor!
        // 🚀 ŞAHİN GÖZÜ ZIRHI: RAM ŞİŞMESİNİ SIFIRA İNDİREN YENİ RAPORLAMA MOTORU
        [HttpGet("PatronCariRaporu")]
        public async Task<IActionResult> GetPatronCariRaporu([FromQuery] string? ay = null, [FromQuery] bool tumunuGetir = false, CancellationToken cancellationToken = default)
        {
            var patronlar = await _context.Patronlar.AsNoTracking().Select(p => new { p.Id, p.Ad, p.Soyad }).ToListAsync(cancellationToken);

            DateTime ayBasi = DateTime.MinValue;
            DateTime aySonu = DateTime.MaxValue;

            if (!string.IsNullOrWhiteSpace(ay) && DateTime.TryParseExact(ay, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var seciliTarih))
            {
                ayBasi = new DateTime(seciliTarih.Year, seciliTarih.Month, 1);
                aySonu = ayBasi.AddMonths(1);
            }

            var rapor = new List<PatronCariRaporDto>();

            // Patron sayısı az olduğu için (Örn: 2-3 kişi) döngüye girmek performansı bozmaz, aksine milyon satırı RAM'e almaktan kurtarır!
            foreach (var p in patronlar)
            {
                // 🚀 1. SİHİRLİ DOKUNUŞ: MİLAT (SIFIRLAMA) TARİHİNİ SQL'DE BULUYORUZ!
                DateTime milatTarihi = DateTime.MinValue;

                if (!tumunuGetir) // Eğer Excel değilse, sadece günceli istiyorsak
                {
                    // Eski kayıtlar için "Aciklama.Contains" zırhını da tuttuk ki geçmiş verilerin kaybolmasın.
                    var sonSifirlamaTarihi = await _context.KasaHareketleri
                        .AsNoTracking()
                        .Where(h => h.PatronId == p.Id && !h.IsDeleted &&
                                    (h.SifirlamaFisiMi || (h.Aciklama != null && (h.Aciklama.Contains("KÂR DAĞITIMI") || h.Aciklama.Contains("KAR DAĞITIMI")))))
                        .MaxAsync(h => (DateTime?)h.IslemTarihi, cancellationToken);

                    if (sonSifirlamaTarihi.HasValue)
                    {
                        milatTarihi = sonSifirlamaTarihi.Value; // Miladı bulduk!
                    }
                }

                // 🚀 2. SİHİRLİ DOKUNUŞ: RAM'e 50.000 satır değil, sadece MİLAT'tan sonrakileri alıyoruz!
                var hareketQuery = _context.KasaHareketleri
                    .AsNoTracking()
                    .Where(h => h.PatronId == p.Id && !h.IsDeleted && h.IslemTarihi > milatTarihi);

                if (ayBasi != DateTime.MinValue)
                {
                    hareketQuery = hareketQuery.Where(h => h.IslemTarihi >= ayBasi && h.IslemTarihi < aySonu);
                }

                var patronHareketleri = await hareketQuery
                    .Select(h => new
                    {
                        h.Tutar,
                        h.Yon,
                        SantiyeAd = h.Santiye != null ? h.Santiye.Ad : "Genel Merkez"
                    })
                    .ToListAsync(cancellationToken);

                // Artık elimizde sadece o 5-10 satırlık taze veri var, matematiği uçarak yapıyoruz:
                decimal toplamGiris = patronHareketleri.Where(h => h.Yon == KasaIslemYonu.Giris).Sum(h => h.Tutar);
                decimal toplamCikis = patronHareketleri.Where(h => h.Yon == KasaIslemYonu.Cikis).Sum(h => h.Tutar);

                var santiyeBazliGiderler = patronHareketleri.GroupBy(h => h.SantiyeAd)
                    .Select(g => new SantiyeHarcamaDto
                    {
                        SantiyeAd = g.Key,
                        Harcama = g.Where(x => x.Yon == KasaIslemYonu.Giris).Sum(x => x.Tutar) - g.Where(x => x.Yon == KasaIslemYonu.Cikis).Sum(x => x.Tutar)
                    }).ToList();

                if (!tumunuGetir)
                {
                    santiyeBazliGiderler = santiyeBazliGiderler.Where(s => s.Harcama != 0).ToList();
                }

                rapor.Add(new PatronCariRaporDto
                {
                    PatronAd = $"{p.Ad} {p.Soyad}".Trim(),
                    ToplamSermaye = toplamGiris,
                    ToplamCikis = toplamCikis,
                    ToplamHarcama = toplamGiris - toplamCikis,
                    SantiyeBazli = santiyeBazliGiderler
                });
            }

            return Ok(rapor);
        }


        // 🚀 BABANIN İSTEDİĞİ KASA SIFIRLAMA VE KÂR DAĞITIM MOTORU
        // 🚀 BABANIN İSTEDİĞİ ŞANTİYE BAZLI KASA SIFIRLAMA VE KÂR DAĞITIM MOTORU
        // 🚀 BABANIN İSTEDİĞİ ŞANTİYE BAZLI KASA SIFIRLAMA MOTORU (BANKA DAHİL, KÂR HARİÇ)
        [HttpPost("KasariSifirlaVeKarDagit")]
        public async Task<IActionResult> KasariSifirlaVeKarDagit()
        {
            var patronlar = await _context.Patronlar
                .Where(p => p.KasaId != null)
                .OrderBy(p => p.Id) // 🚀 ZIRH: İlk eklenen kasayı bulabilmek için ID'ye göre sıraladık
                .ToListAsync();

            if (!patronlar.Any())
                return BadRequest(new { mesaj = "Sistemde tanımlı kasa bulunamadı." });

            // 🚀 SENİOR ZIRHI: İLK EKLENEN KASAYI "BANKA (ANA KASA)" OLARAK TESPİT ET
            var anaKasaBanka = patronlar.First();

            // ✅ Tüm hareketleri tek sorguda çek
            var kasaIdleri = patronlar.Select(p => p.KasaId).ToList();
            var tumHareketler = await _context.KasaHareketleri
                .Where(h => kasaIdleri.Contains(h.KasaId) && !h.IsDeleted)
                .ToListAsync();

            // ✅ Tüm şantiye adlarını tek sorguda çek
            var santiyeIdleri = tumHareketler
                .Where(h => h.SantiyeId.HasValue)
                .Select(h => h.SantiyeId!.Value)
                .Distinct()
                .ToList();

            var santiyeAdlari = await _context.Santiyeler
                .Where(s => santiyeIdleri.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Ad);

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                DateTime islemZamani = ZamanMotoru.SimdiTurkiye();
                int islemSayisi = 0;
                var yeniHareketler = new List<KasaHareketi>();

                foreach (var p in patronlar)
                {
                    // 🚀 YENİ ZIRH: Bu Kasa Banka mı Yoksa Ortak mı?
                    bool isBanka = (p.Id == anaKasaBanka.Id);

                    // ✅ DB'ye gitme, bellekteki veriyi filtrele
                    var pHareketler = tumHareketler.Where(h => h.KasaId == p.KasaId).ToList();
                    var santiyeGruplari = pHareketler.GroupBy(h => h.SantiyeId).ToList();

                    foreach (var grup in santiyeGruplari)
                    {
                        decimal toplamGiris = grup.Where(h => h.Yon == KasaIslemYonu.Giris).Sum(h => h.Tutar);
                        decimal toplamCikis = grup.Where(h => h.Yon == KasaIslemYonu.Cikis).Sum(h => h.Tutar);
                        decimal bakiye = toplamGiris - toplamCikis;

                        if (bakiye == 0) continue;

                        // ✅ Dictionary'den çek, DB'ye gitme
                        string santiyeAdi = grup.Key.HasValue
                            ? santiyeAdlari.GetValueOrDefault(grup.Key.Value, "Bilinmeyen Şantiye")
                            : "Genel Merkez";

                        // 🚀 DAHİYANE DOKUNUŞ: Ortaklara kâr dağıtım fişi, Bankaya düz sıfırlama fişi kesiyoruz!
                        string fisAciklamasi = isBanka
                            ? $"🏦 DÖNEM SONU KASA SIFIRLAMASI ({santiyeAdi})"
                            : $"💰 DÖNEM SONU KÂR DAĞITIMI ({santiyeAdi} Sıfırlaması)";

                        yeniHareketler.Add(new KasaHareketi
                        {
                            KasaId = p.KasaId ?? 0,
                            PatronId = p.Id,
                            SantiyeId = grup.Key,
                            Tutar = Math.Abs(bakiye),
                            Yon = bakiye > 0 ? KasaIslemYonu.Cikis : KasaIslemYonu.Giris,
                            IslemTarihi = islemZamani,
                            Aciklama = fisAciklamasi,
                            SifirlamaFisiMi = true // Sistemi bu kolonla yönetiyoruz
                        });

                        islemSayisi++;
                    }
                }

                if (yeniHareketler.Any())
                {
                    await _context.KasaHareketleri.AddRangeAsync(yeniHareketler);
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();

                return Ok(new { mesaj = $"Başarıyla kâr payı dağıtıldı ve {islemSayisi} adet şantiye kasası (Banka dahil) sıfırlandı." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { mesaj = "Sıfırlama sırasında hata oluştu: " + ex.Message });
            }
        }
        // 🏗️ ŞANTİYE GENEL MALİYET RAPORU (TÜM ZAMANLAR - SIFIRLAMALARI GÖRMEZDEN GELİR)
        [HttpGet("SantiyeGenelMaliyetRaporu")]
        public async Task<IActionResult> GetSantiyeGenelMaliyetRaporu(CancellationToken cancellationToken = default)
        {
            var santiyeler = await _context.Santiyeler.AsNoTracking().Select(s => new { s.Id, s.Ad }).ToListAsync(cancellationToken);

            // 🚀 ZIRH: "DÖNEM SONU KÂR DAĞITIMI" yazan sıfırlama fişlerini tamamen filtrele.
            // Sadece gerçek harcamaları (Maaş, Avans, Malzeme vs.) al.
            var hareketler = await _context.KasaHareketleri.AsNoTracking()
                .Where(h => !h.IsDeleted && (h.Aciklama == null || !h.Aciklama.Contains("DÖNEM SONU KÂR DAĞITIMI")))
                .Select(h => new { h.SantiyeId, h.Tutar, h.Yon, h.HareketTipi })
                .ToListAsync(cancellationToken);

            var rapor = santiyeler.Select(s =>
            {
                var sh = hareketler.Where(h => h.SantiyeId == s.Id).ToList();
                decimal toplamGider = sh.Where(h => h.Yon == KasaIslemYonu.Cikis).Sum(h => h.Tutar);
                decimal toplamGelir = sh.Where(h => h.Yon == KasaIslemYonu.Giris).Sum(h => h.Tutar); // Şantiyeye dışarıdan giren para (Nadirdir)

                return new
                {
                    SantiyeAd = s.Ad,
                    ToplamGider = toplamGider,
                    ToplamGelir = toplamGelir,
                    NetMaliyet = toplamGider - toplamGelir // Gerçek cepten çıkan para
                };
            }).Where(r => r.ToplamGider > 0 || r.ToplamGelir > 0)
              .OrderByDescending(r => r.NetMaliyet)
              .ToList();

            // Bir de "Genel Merkez" (Şantiyesiz giderler) ekleyelim
            var merkezHareketleri = hareketler.Where(h => h.SantiyeId == null).ToList();
            if (merkezHareketleri.Any())
            {
                decimal mGider = merkezHareketleri.Where(h => h.Yon == KasaIslemYonu.Cikis).Sum(h => h.Tutar);
                decimal mGelir = merkezHareketleri.Where(h => h.Yon == KasaIslemYonu.Giris).Sum(h => h.Tutar);
                rapor.Add(new
                {
                    SantiyeAd = "🏢 Genel Merkez (Şantiyesiz)",
                    ToplamGider = mGider,
                    ToplamGelir = mGelir,
                    NetMaliyet = mGider - mGelir
                });
            }

            return Ok(rapor);
        }



        [HttpGet("AylikOzetExcel")]
        public async Task<IActionResult> GetAylikOzetExcel([FromQuery] string ay)
        {
            var actionResult = await GetAylikBordro(ay, tumunuGetir: true);

            if (actionResult is not OkObjectResult okResult || okResult.Value is not List<BordroOzetDto> veriler || !veriler.Any())
            {
                return BadRequest(new { mesaj = "Bu döneme ait bordro kaydı bulunamadı." });
            }

            // 🚀 SENİOR ZIRHI: Excel Asgari Ödeme Ayrıştırıcısı İçin Tarih Sınırlarını Belirliyoruz
            DateTime zAyBasi = DateTime.MinValue;
            DateTime zAySonu = DateTime.MaxValue;
            if (!string.IsNullOrWhiteSpace(ay) && ay != "tumu" && DateTime.TryParseExact(ay, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var zSeciliTarih))
            {
                zAyBasi = new DateTime(zSeciliTarih.Year, zSeciliTarih.Month, 1);
                zAySonu = zAyBasi.AddMonths(1);
            }

            // 🚀 Avansları Excel için ham halde çekiyoruz (Asgari ile Normali Ayırmak İçin)
            var avansQuery = _context.Avanslar.Where(a => !a.IsDeleted);
            if (zAyBasi != DateTime.MinValue)
            {
                avansQuery = avansQuery.Where(a => a.Tarih >= zAyBasi && a.Tarih < zAySonu);
            }
            var tumAvanslarExcelIcin = await avansQuery.ToListAsync();

            using var workbook = new XLWorkbook();

            // =====================================================================
            // 🚀 1. SAYFA: AYLIK BORDRO (BABANIN İSTEDİĞİ 14 SÜTUNLU YENİ FORMAT)
            // =====================================================================
            var worksheet = workbook.Worksheets.Add("Aylık_Bordro");

            // 🌟 İŞTE BABANIN İSTEDİĞİ YENİ BAŞLIKLAR (TAM 14 ADET)
            string[] basliklar = {
                "Sıra", "Usta Adı Soyadı", "Meslek", "Şantiye", "Yevmiye (₺)",
                "Tam (1.0)", "Yarım (0.5)", "Mesaili (1.5)", "Çift (2.0)", "Gelmedi (0)",
                "Toplam Hakediş (₺)", "Asgari Ödenen (₺)", "Avans Ödenen (₺)", "Net Ödenecek (₺)"
            };

            for (int i = 0; i < basliklar.Length; i++)
            {
                var hucre = worksheet.Cell(1, i + 1);
                hucre.Value = basliklar[i];
                hucre.Style.Font.Bold = true;
                hucre.Style.Font.FontColor = XLColor.White;
                hucre.Style.Fill.BackgroundColor = XLColor.MidnightBlue;
                hucre.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            string paraFormati = "#,##0.00 \"₺\";-#,##0.00 \"₺\"";

            int satir = 2, siraNo = 1;
            var tekSantiyeliler = veriler.Where(v => v.SantiyeDetaylari == null || v.SantiyeDetaylari.Count <= 1).ToList();
            var cokluSantiyeliler = veriler.Where(v => v.SantiyeDetaylari != null && v.SantiyeDetaylari.Count > 1).ToList();

            // 🚀 SENİOR ZIRHI: O(n²) RAM şişmesini önlemek için Asgari Ödemeleri 
            // tek seferde işçi bazlı gruplayıp Sözlük (Dictionary) yapıyoruz! (O(1) Hız)
            var isciAsgariSozlugu = tumAvanslarExcelIcin
                .Where(a => a.Aciklama != null && (a.Aciklama.ToLower().Contains("asgari") || a.Aciklama.ToLower().Contains("hızlı ödeme")))
                .GroupBy(a => a.IsciId)
                .ToDictionary(g => g.Key, g => g.Sum(x => (decimal)x.Tutar));

            var isciSantiyeAsgariSozlugu = tumAvanslarExcelIcin
                .Where(a => a.Aciklama != null && (a.Aciklama.ToLower().Contains("asgari") || a.Aciklama.ToLower().Contains("hızlı ödeme")))
                .GroupBy(a => new { a.IsciId, SantiyeId = a.SantiyeId ?? 0 })
                .ToDictionary(g => $"{g.Key.IsciId}-{g.Key.SantiyeId}", g => g.Sum(x => (decimal)x.Tutar));

            foreach (var v in tekSantiyeliler)
            {
                decimal asgariTutar = isciAsgariSozlugu.TryGetValue(v.Id, out var tutar) ? tutar : 0;
                decimal kalanAlacak = v.SantiyeDetaylari != null && v.SantiyeDetaylari.Any() ? v.SantiyeDetaylari.Sum(d => d.Bakiye) : 0;
                decimal toplamOdenen = v.Hakedis - kalanAlacak;
                decimal normalAvansVeOdenen = Math.Max(0, toplamOdenen - asgariTutar);

                worksheet.Cell(satir, 1).Value = siraNo;
                worksheet.Cell(satir, 2).Value = v.AdSoyad;
                worksheet.Cell(satir, 3).Value = v.Meslek;
                worksheet.Cell(satir, 4).Value = string.IsNullOrWhiteSpace(v.SantiyeAd) ? "Belirtilmemiş" : v.SantiyeAd;
                worksheet.Cell(satir, 5).Value = v.Yevmiye;
                worksheet.Cell(satir, 5).Style.NumberFormat.Format = paraFormati;
                worksheet.Cell(satir, 6).Value = v.TamGun;
                worksheet.Cell(satir, 7).Value = v.YarimGun;
                worksheet.Cell(satir, 8).Value = v.MesailiGun;
                worksheet.Cell(satir, 9).Value = v.CiftYevmiyeGun;
                worksheet.Cell(satir, 10).Value = v.GelmediGun;

                worksheet.Cell(satir, 11).Value = v.Hakedis;
                worksheet.Cell(satir, 11).Style.NumberFormat.Format = paraFormati;

                worksheet.Cell(satir, 12).Value = asgariTutar;
                worksheet.Cell(satir, 12).Style.NumberFormat.Format = paraFormati;
                worksheet.Cell(satir, 12).Style.Font.FontColor = XLColor.RoyalBlue;

                worksheet.Cell(satir, 13).Value = normalAvansVeOdenen;
                worksheet.Cell(satir, 13).Style.NumberFormat.Format = paraFormati;
                worksheet.Cell(satir, 13).Style.Font.FontColor = XLColor.DarkOrange;

                worksheet.Cell(satir, 14).Value = kalanAlacak;
                worksheet.Cell(satir, 14).Style.Font.Bold = true;
                worksheet.Cell(satir, 14).Style.NumberFormat.Format = paraFormati;

                if (kalanAlacak <= 0)
                {
                    worksheet.Range(satir, 1, satir, 14).Style.Fill.BackgroundColor = XLColor.WhiteSmoke;
                    worksheet.Cell(satir, 14).Style.Font.FontColor = XLColor.SeaGreen;
                }
                else
                {
                    worksheet.Cell(satir, 14).Style.Font.FontColor = XLColor.DarkRed;
                }

                satir++; siraNo++;
            }

            if (cokluSantiyeliler.Any())
            {
                satir++;
                var ayirici = worksheet.Range(satir, 1, satir, 14);
                ayirici.Merge().Value = "⏬ BİRDEN FAZLA ŞANTİYEDE GÖREVLENDİRİLEN USTALAR (ŞANTİYE BAZLI MALİYETLERİ) ⏬";
                ayirici.Style.Font.Bold = true;
                ayirici.Style.Font.FontColor = XLColor.White;
                ayirici.Style.Fill.BackgroundColor = XLColor.DarkOrange;
                ayirici.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                satir++;

                foreach (var v in cokluSantiyeliler)
                {
                    decimal genelKalan = v.SantiyeDetaylari != null && v.SantiyeDetaylari.Any() ? v.SantiyeDetaylari.Sum(d => d.Bakiye) : 0;
                    decimal genelAsgari = isciAsgariSozlugu.TryGetValue(v.Id, out var ga) ? ga : 0;
                    decimal genelToplamOdenen = v.Hakedis - genelKalan;
                    decimal genelNormalAvans = Math.Max(0, genelToplamOdenen - genelAsgari);

                    foreach (var detay in v.SantiyeDetaylari ?? Enumerable.Empty<SantiyeGunDetay>())
                    {
                        decimal dAsgariTutar = isciSantiyeAsgariSozlugu.TryGetValue($"{v.Id}-{detay.SantiyeId}", out var da) ? da : 0;
                        decimal dKalan = detay.Bakiye;
                        decimal dToplamOdenen = detay.Hakedis - dKalan;
                        decimal dNormalOdeme = Math.Max(0, dToplamOdenen - dAsgariTutar);

                        worksheet.Cell(satir, 2).Value = v.AdSoyad;
                        worksheet.Cell(satir, 3).Value = v.Meslek;
                        worksheet.Cell(satir, 4).Value = detay.SantiyeAd;
                        worksheet.Cell(satir, 5).Value = v.Yevmiye;
                        worksheet.Cell(satir, 5).Style.NumberFormat.Format = paraFormati;
                        worksheet.Cell(satir, 6).Value = detay.TamGun;
                        worksheet.Cell(satir, 7).Value = detay.YarimGun;
                        worksheet.Cell(satir, 8).Value = detay.MesailiGun;
                        worksheet.Cell(satir, 9).Value = detay.CiftYevmiyeGun;
                        worksheet.Cell(satir, 10).Value = detay.GelmediGun;

                        worksheet.Cell(satir, 11).Value = detay.Hakedis;
                        worksheet.Cell(satir, 11).Style.NumberFormat.Format = paraFormati;

                        worksheet.Cell(satir, 12).Value = dAsgariTutar;
                        worksheet.Cell(satir, 12).Style.NumberFormat.Format = paraFormati;
                        worksheet.Cell(satir, 12).Style.Font.FontColor = XLColor.RoyalBlue;

                        worksheet.Cell(satir, 13).Value = dNormalOdeme;
                        worksheet.Cell(satir, 13).Style.NumberFormat.Format = paraFormati;
                        worksheet.Cell(satir, 13).Style.Font.FontColor = XLColor.DarkOrange;

                        worksheet.Cell(satir, 14).Value = dKalan;
                        worksheet.Cell(satir, 14).Style.NumberFormat.Format = paraFormati;

                        worksheet.Range(satir, 2, satir, 11).Style.Font.Italic = true;
                        satir++;
                    }

                    worksheet.Cell(satir, 1).Value = siraNo;
                    worksheet.Cell(satir, 2).Value = $"{v.AdSoyad} (GENEL TOPLAM)";
                    worksheet.Cell(satir, 2).Style.Font.Bold = true;
                    worksheet.Cell(satir, 2).Style.Font.FontColor = XLColor.DarkRed;

                    worksheet.Cell(satir, 4).Value = $"({v.SantiyeDetaylari?.Count ?? 0} Şantiye Özeti)";
                    worksheet.Cell(satir, 4).Style.Font.Bold = true;

                    worksheet.Cell(satir, 11).Value = v.Hakedis;
                    worksheet.Cell(satir, 11).Style.NumberFormat.Format = paraFormati;
                    worksheet.Cell(satir, 11).Style.Font.Bold = true;

                    worksheet.Cell(satir, 12).Value = genelAsgari;
                    worksheet.Cell(satir, 12).Style.NumberFormat.Format = paraFormati;
                    worksheet.Cell(satir, 12).Style.Font.Bold = true;
                    worksheet.Cell(satir, 12).Style.Font.FontColor = XLColor.RoyalBlue;

                    worksheet.Cell(satir, 13).Value = genelNormalAvans;
                    worksheet.Cell(satir, 13).Style.NumberFormat.Format = paraFormati;
                    worksheet.Cell(satir, 13).Style.Font.Bold = true;
                    worksheet.Cell(satir, 13).Style.Font.FontColor = XLColor.DarkOrange;

                    worksheet.Cell(satir, 14).Value = genelKalan;
                    worksheet.Cell(satir, 14).Style.Font.Bold = true;
                    worksheet.Cell(satir, 14).Style.NumberFormat.Format = paraFormati;

                    worksheet.Range(satir, 1, satir, 14).Style.Fill.BackgroundColor = genelKalan <= 0 ? XLColor.WhiteSmoke : XLColor.LightYellow;
                    if (genelKalan <= 0) worksheet.Cell(satir, 14).Style.Font.FontColor = XLColor.SeaGreen;
                    else worksheet.Cell(satir, 14).Style.Font.FontColor = XLColor.DarkRed;

                    satir++; siraNo++;
                }
            }

            worksheet.Columns().AdjustToContents();
            worksheet.Column(5).Width = 15;
            worksheet.Column(11).Width = 18;
            worksheet.Column(12).Width = 18;
            worksheet.Column(13).Width = 18;
            worksheet.Column(14).Width = 18;

            // =====================================================================
            // 🚀 ORTAK KASA VERİLERİNİ TEK SEFERDE ÇEKİYORUZ (Sistem Fişleri GİZLENDİ)
            // =====================================================================
            var tumHareketlerQuery = _context.KasaHareketleri
                .Include(h => h.Patron)
                .Include(h => h.Santiye)
                .Where(h => !h.IsDeleted && !h.SifirlamaFisiMi); // 🛡️ İŞTE SİHRİN GERÇEKLEŞTİĞİ YER

            if (zAyBasi != DateTime.MinValue)
            {
                tumHareketlerQuery = tumHareketlerQuery.Where(h => h.IslemTarihi >= zAyBasi && h.IslemTarihi < zAySonu);
            }

            var tumHareketler = await tumHareketlerQuery.OrderBy(h => h.IslemTarihi).ToListAsync();

            // =====================================================================
            // 🚀 2. SAYFA: BABANIN İSTEDİĞİ ŞANTİYE BAZLI GERÇEK MALİYET RAPORU
            // =====================================================================
            var wsZraporu = workbook.Worksheets.Add("Patron_Z_Raporu");
            wsZraporu.Cell(1, 1).Value = "Kasa / Patron Adı";
            wsZraporu.Cell(1, 2).Value = "Şantiye Adı";
            wsZraporu.Cell(1, 3).Value = "Bu Ay Giren Para (+)";
            wsZraporu.Cell(1, 4).Value = "Bu Ay Çıkan Para (-)";
            wsZraporu.Cell(1, 5).Value = "NET Durum (Giren - Çıkan)";

            var zRange = wsZraporu.Range(1, 1, 1, 5);
            zRange.Style.Font.Bold = true;
            zRange.Style.Font.FontColor = XLColor.White;
            zRange.Style.Fill.BackgroundColor = XLColor.DarkGreen;
            zRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int zSatir = 2;

            var tumPatronlar = await _context.Patronlar.OrderBy(p => p.Id).ToListAsync();
            var anaKasaBanka = tumPatronlar.FirstOrDefault();

            if (tumPatronlar.Any())
            {
                foreach (var patron in tumPatronlar)
                {
                    bool isBanka = anaKasaBanka != null && patron.Id == anaKasaBanka.Id;
                    string patronAdSoyad = $"{patron.Ad} {patron.Soyad}";

                    if (isBanka)
                    {
                        wsZraporu.Range(zSatir, 1, zSatir, 5).Merge().Value = $"🏦 ANA KASA (BANKA): {patronAdSoyad}  [Kâr Dağıtımına Dahil Değil]";
                        wsZraporu.Range(zSatir, 1, zSatir, 5).Style.Fill.BackgroundColor = XLColor.DarkOrange;
                        wsZraporu.Range(zSatir, 1, zSatir, 5).Style.Font.FontColor = XLColor.White;
                    }
                    else
                    {
                        wsZraporu.Range(zSatir, 1, zSatir, 5).Merge().Value = $"💰 ORTAK KASASI: {patronAdSoyad}";
                        wsZraporu.Range(zSatir, 1, zSatir, 5).Style.Fill.BackgroundColor = XLColor.LightGray;
                        wsZraporu.Range(zSatir, 1, zSatir, 5).Style.Font.FontColor = XLColor.Black;
                    }

                    wsZraporu.Range(zSatir, 1, zSatir, 5).Style.Font.Bold = true;
                    zSatir++;

                    // SifirlamaFisiMi sorguda filtrelendiği için ekstradan Aciklama kontrolüne gerek yok ama çift dikiş kalsın.
                    var pGrup = tumHareketler
                        .Where(h => h.PatronId == patron.Id &&
                                   (h.Aciklama == null || (!h.Aciklama.ToLower().Contains("kâr dağıtımı") && !h.Aciklama.ToLower().Contains("kar dağitimi"))))
                        .ToList();

                    if (pGrup.Any())
                    {
                        var zSantiyeGruplari = pGrup.GroupBy(h => h.SantiyeId).ToList();
                        decimal patronToplamGiris = 0, patronToplamCikis = 0;

                        foreach (var sGrup in zSantiyeGruplari)
                        {
                            string? sAd = sGrup.FirstOrDefault()?.Santiye?.Ad;
                            string santiyeAd = !string.IsNullOrWhiteSpace(sAd) ? $"🌍 {sAd}" : "🏢 Genel Merkez / Ofis";

                            decimal sGiris = sGrup.Where(h => h.Yon == KasaIslemYonu.Giris).Sum(h => h.Tutar);
                            decimal sCikis = sGrup.Where(h => h.Yon == KasaIslemYonu.Cikis).Sum(h => h.Tutar);
                            decimal sNet = sGiris - sCikis;

                            patronToplamGiris += sGiris;
                            patronToplamCikis += sCikis;

                            wsZraporu.Cell(zSatir, 1).Value = "-";
                            wsZraporu.Cell(zSatir, 2).Value = santiyeAd;

                            wsZraporu.Cell(zSatir, 3).Value = sGiris;
                            wsZraporu.Cell(zSatir, 3).Style.NumberFormat.Format = paraFormati;
                            wsZraporu.Cell(zSatir, 3).Style.Font.FontColor = XLColor.SeaGreen;

                            wsZraporu.Cell(zSatir, 4).Value = sCikis;
                            wsZraporu.Cell(zSatir, 4).Style.NumberFormat.Format = paraFormati;
                            wsZraporu.Cell(zSatir, 4).Style.Font.FontColor = XLColor.DarkRed;

                            wsZraporu.Cell(zSatir, 5).Value = sNet;
                            wsZraporu.Cell(zSatir, 5).Style.NumberFormat.Format = paraFormati;
                            wsZraporu.Cell(zSatir, 5).Style.Font.Bold = true;
                            zSatir++;
                        }

                        wsZraporu.Cell(zSatir, 2).Value = "KASA TOPLAMI:";
                        wsZraporu.Cell(zSatir, 2).Style.Font.Bold = true;

                        wsZraporu.Cell(zSatir, 3).Value = patronToplamGiris;
                        wsZraporu.Cell(zSatir, 3).Style.NumberFormat.Format = paraFormati;
                        wsZraporu.Cell(zSatir, 3).Style.Font.Bold = true;

                        wsZraporu.Cell(zSatir, 4).Value = patronToplamCikis;
                        wsZraporu.Cell(zSatir, 4).Style.NumberFormat.Format = paraFormati;
                        wsZraporu.Cell(zSatir, 4).Style.Font.Bold = true;

                        wsZraporu.Cell(zSatir, 5).Value = patronToplamGiris - patronToplamCikis;
                        wsZraporu.Cell(zSatir, 5).Style.NumberFormat.Format = paraFormati;
                        wsZraporu.Cell(zSatir, 5).Style.Font.Bold = true;
                        wsZraporu.Range(zSatir, 2, zSatir, 5).Style.Fill.BackgroundColor = XLColor.WhiteSmoke;
                    }
                    else
                    {
                        wsZraporu.Cell(zSatir, 2).Value = "Bu döneme ait harcama/hareket bulunamadı.";
                        wsZraporu.Cell(zSatir, 2).Style.Font.Italic = true;
                        wsZraporu.Cell(zSatir, 2).Style.Font.FontColor = XLColor.Gray;
                    }

                    zSatir += 2;
                }
            }
            else
            {
                wsZraporu.Cell(2, 1).Value = "Sistemde kayıtlı kasa/patron bulunamadı.";
            }

            wsZraporu.Columns().AdjustToContents();
            wsZraporu.Column(3).Width = 25;
            wsZraporu.Column(4).Width = 25;
            wsZraporu.Column(5).Width = 25;

            // =====================================================================
            // 🚀 3. SAYFA: KASA DETAY DÖKÜMÜ (Fişler tamamen gizlendi)
            // =====================================================================
            var wsDetay = workbook.Worksheets.Add("Kasa_Detay_Dokumu");

            wsDetay.Cell(1, 1).Value = "Tarih";
            wsDetay.Cell(1, 2).Value = "Patron / Kasa";
            wsDetay.Cell(1, 3).Value = "İşlem Yönü";
            wsDetay.Cell(1, 4).Value = "Tutar (₺)";
            wsDetay.Cell(1, 5).Value = "Şantiye";
            wsDetay.Cell(1, 6).Value = "Açıklama / Kime Verildi";

            var detayRange = wsDetay.Range(1, 1, 1, 6);
            detayRange.Style.Font.Bold = true;
            detayRange.Style.Font.FontColor = XLColor.White;
            detayRange.Style.Fill.BackgroundColor = XLColor.MidnightBlue;

            int detaySatir = 2;

            foreach (var h in tumHareketler)
            {
                wsDetay.Cell(detaySatir, 1).Value = h.IslemTarihi.ToString("dd.MM.yyyy HH:mm");
                wsDetay.Cell(detaySatir, 2).Value = h.Patron != null ? $"{h.Patron.Ad} {h.Patron.Soyad}" : "Belirtilmemiş";

                var yonCell = wsDetay.Cell(detaySatir, 3);
                yonCell.Value = h.Yon == KasaIslemYonu.Giris ? "GİRİŞ (+)" : "ÇIKIŞ (-)";
                yonCell.Style.Font.FontColor = h.Yon == KasaIslemYonu.Giris ? XLColor.SeaGreen : XLColor.DarkRed;
                yonCell.Style.Font.Bold = true;

                var tutarCell = wsDetay.Cell(detaySatir, 4);
                tutarCell.Value = h.Tutar;
                tutarCell.Style.NumberFormat.Format = paraFormati;
                tutarCell.Style.Font.FontColor = h.Yon == KasaIslemYonu.Giris ? XLColor.SeaGreen : XLColor.DarkRed;

                wsDetay.Cell(detaySatir, 5).Value = h.Santiye != null ? h.Santiye.Ad : "Genel Merkez";
                wsDetay.Cell(detaySatir, 6).Value = h.Aciklama;

                detaySatir++;
            }

            wsDetay.Columns().AdjustToContents();
            wsDetay.Column(4).Width = 25;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Santiye_Muhasebe_{ay}.xlsx");
        }


        [HttpPost("BorcIadeAl")]
        public async Task<IActionResult> BorcIadeAl([FromBody] BorcIadeRequest request)
        {
            if (request == null || request.IsciId <= 0 || request.PatronId <= 0) return BadRequest(new { mesaj = "Eksik veri gönderildi patron!" });

            try
            {
                var isci = await _context.Isciler.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.IsciId);

                if (isci == null) return NotFound(new { mesaj = "İşçi bulunamadı!" });

                var patron = await _context.Patronlar.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PatronId);

                // 🚀 ZIRH: Eğer patron null ise VEYA KasaId'si yoksa anında durdur!
                // Ama eğer varsa, o KasaId'yi 'guvenliKasaId' adında sapasağlam (null olmayan) bir int'e dönüştür!
                if (patron is not { KasaId: int guvenliKasaId })
                    return BadRequest(new { mesaj = "Patronun kasası tanımlı değil!" });
                // 🛡️ ZIRH: DÜNYANIN SONUNA KADAR MÜHÜRLEME ENGELLEYİCİ!
                if (string.IsNullOrWhiteSpace(request.Ay) || request.Ay == "tumu")
                {
                    return BadRequest(new { mesaj = "Hop Patron! Nakit iade alırken mutlaka belirli bir AY seçmelisin. 'Tüm Şantiyeler/Tüm Zamanlar' seçiliyken iade mühürlemesi yapılamaz!" });
                }

                DateTime kesimTarihiSiniri;
                if (DateTime.TryParseExact(request.Ay, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var seciliTarih))
                {
                    kesimTarihiSiniri = new DateTime(seciliTarih.Year, seciliTarih.Month, 1).AddMonths(1);
                }
                else
                {
                    // Olur da saçma sapan bir tarih gelirse diye son güvenlik:
                    return BadRequest(new { mesaj = "Geçersiz bir ay formatı gönderildi!" });
                }

                // 🛡️ ZIRH 1: MATEMATİK YAPARKEN SADECE SEÇİLİ ŞANTİYEYİ HESAPLA
                var toplamHakedis = await _context.GunlukKayitlar
                    .Where(g => g.IsciId == request.IsciId && g.SantiyeId == request.SantiyeId && !g.OdendiMi && g.Tarih < kesimTarihiSiniri)
                    .SumAsync(g => (decimal?)g.Yevmiye) ?? 0;

                var toplamAvans = await _context.Avanslar
                    .Where(a => a.IsciId == request.IsciId && a.SantiyeId == request.SantiyeId && !a.OdendiMi && a.Tarih < kesimTarihiSiniri)
                    .SumAsync(a => (decimal?)a.Tutar) ?? 0;

                var netKalan = toplamHakedis - toplamAvans;
                if (netKalan >= 0) return BadRequest(new { mesaj = "Bu ustanın bu şantiyede bize borcu yok ki iade alalım!" });

                decimal iadeTutari = Math.Abs(netKalan);

                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    DateTime islemTarihi = ZamanMotoru.SimdiTurkiye();

                    // 🛡️ ZIRH 2: KASAYA GİREN PARAYI VE İADE AVANSINI ŞANTİYEYE BAĞLA
                    var kasaHareketi = new KasaHareketi { KasaId = guvenliKasaId, PatronId = patron.Id, SantiyeId = request.SantiyeId, Tutar = iadeTutari, Yon = KasaIslemYonu.Giris, HareketTipi = KasaHareketTipi.ManuelGelir, IslemTarihi = islemTarihi, Aciklama = $"{isci.Ad} {isci.Soyad} - Nakit İade. Not: {request.Aciklama}" };

                    await _context.KasaHareketleri.AddAsync(kasaHareketi);

                    var iadeAvansi = new Avans { IsciId = request.IsciId, KasaId = guvenliKasaId, SantiyeId = request.SantiyeId, Tutar = -iadeTutari, Tarih = islemTarihi, OdemeTuru = "Nakit İade", Aciklama = $"İade. Not: {request.Aciklama}", OdendiMi = true, IsDeleted = false };
                    await _context.Avanslar.AddAsync(iadeAvansi);

                    // 🛡️ ZIRH 3: MÜHÜRLERKEN SADECE O ŞANTİYEYİ MÜHÜRLE!
                    await _context.GunlukKayitlar
                        .Where(g => g.IsciId == request.IsciId && g.SantiyeId == request.SantiyeId && !g.OdendiMi && g.Tarih < kesimTarihiSiniri)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.OdendiMi, true));

                    await _context.Avanslar
                        .Where(a => a.IsciId == request.IsciId && a.SantiyeId == request.SantiyeId && !a.OdendiMi && a.Tarih < kesimTarihiSiniri)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.OdendiMi, true));

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Ok(new { mesaj = $"{iadeTutari:N2} ₺ tahsil edildi, bu şantiyedeki borç sıfırlandı." });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw new Exception("Veritabanı mühürlenirken hata: " + (ex.InnerException?.Message ?? ex.Message));
                }
            }
            catch (Exception ex) { return StatusCode(500, new { mesaj = "Sistem Hatası: " + ex.Message }); }
        }






        [HttpPost("BorcDevret")]
        public async Task<IActionResult> BorcDevret([FromBody] BorcDevretRequest request)
        {
            // 🛡️ 1. ZIRH: TEMEL DOĞRULAMALAR
            if (request == null || request.IsciId <= 0 || request.PatronId <= 0)
                return BadRequest(new { mesaj = "Eksik veri gönderildi!" });

            if (!request.SantiyeId.HasValue)
                return BadRequest(new { mesaj = "Lütfen borcun devredileceği şantiyeyi seçin!" });

            var isci = await _context.Isciler.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.IsciId);
            if (isci == null) return NotFound(new { mesaj = "İşçi bulunamadı." });

            var patron = await _context.Patronlar.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PatronId);
            if (patron is not { KasaId: int guvenliKasaId }) return BadRequest(new { mesaj = "Seçilen patronun kasası tanımlı değil!" });

            // 🛡️ 2. ZIRH: KIYAMET GÜNÜ (OVERFLOW) ENGELLEYİCİ
            if (string.IsNullOrWhiteSpace(request.Ay) || request.Ay == "tumu")
                return BadRequest(new { mesaj = "Hop Patron! Borç devretmek için mutlaka geçerli bir AY seçmelisin. 'Tüm Zamanlar' devredilemez!" });

            DateTime kesimTarihiSiniri;
            if (DateTime.TryParseExact(request.Ay, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var seciliTarih))
            {
                kesimTarihiSiniri = new DateTime(seciliTarih.Year, seciliTarih.Month, 1).AddMonths(1);
            }
            else
            {
                return BadRequest(new { mesaj = "Gönderilen ay formatı geçersiz! (Beklenen: YYYY-MM)" });
            }

            // 🚀 🛡️ 3. ZIRH: MALİ GÜVENLİK (BACKEND HESAPLAMASI)
            // Ekranda yazan tutara asla güvenmiyoruz! Gerçek borcu veritabanından KENDİMİZ hesaplıyoruz.
            var toplamHakedis = await _context.GunlukKayitlar
                .Where(g => g.IsciId == request.IsciId && g.SantiyeId == request.SantiyeId && !g.OdendiMi && g.Tarih < kesimTarihiSiniri)
                .SumAsync(g => (decimal?)g.Yevmiye) ?? 0;

            var toplamAvans = await _context.Avanslar
                .Where(a => a.IsciId == request.IsciId && a.SantiyeId == request.SantiyeId && !a.OdendiMi && a.Tarih < kesimTarihiSiniri)
                .SumAsync(a => (decimal?)a.Tutar) ?? 0;

            var netKalan = toplamHakedis - toplamAvans;

            // Eğer netKalan sıfır veya artıdaysa adamın borcu yoktur, devredemeyiz!
            if (netKalan >= 0)
            {
                return BadRequest(new { mesaj = "Güvenlik İhlali: Bu ustanın bu şantiyede devredilecek bir borcu bulunmuyor!" });
            }

            // Gerçek borç tutarını mutlak değere (artıya) çeviriyoruz
            decimal gercekBorcTutari = Math.Abs(netKalan);

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                DateTime islemTarihi = ZamanMotoru.SimdiTurkiye();

                // 2. KASAYA GİRİŞ (Artık Ekrana değil, kendi bulduğumuz "gercekBorcTutari"na güveniyoruz)



                // 3. AÇIK HESABI SIFIRLAMA
                var iadeAvansi = new Avans
                {
                    IsciId = request.IsciId,
                    KasaId = guvenliKasaId,
                    SantiyeId = request.SantiyeId,
                    Tutar = -gercekBorcTutari, // 👈 GÜVENLİ TUTAR
                    Tarih = islemTarihi,
                    OdemeTuru = "Nakit İade (Devir)",
                    Aciklama = "Açık borç sıfırlandı ve sonraki aya devredildi",
                    OdendiMi = true,
                    IsDeleted = false
                };
                await _context.Avanslar.AddAsync(iadeAvansi);

                // 4. BİR SONRAKİ AYA YENİ BORÇ AÇMA
                var yeniAyBorcu = new Avans
                {
                    IsciId = request.IsciId,
                    KasaId = guvenliKasaId,
                    SantiyeId = request.SantiyeId,
                    Tutar = gercekBorcTutari, // 👈 GÜVENLİ TUTAR
                    Tarih = kesimTarihiSiniri.AddDays(1),
                    OdemeTuru = "Geçmiş Ay Borcu",
                    Aciklama = $"{request.Ay} ayından devreden borç bakiyesi",
                    OdendiMi = false,
                    IsDeleted = false
                };
                await _context.Avanslar.AddAsync(yeniAyBorcu);

                // 5. MÜHÜRLEME
                await _context.GunlukKayitlar
                    .Where(g => g.IsciId == request.IsciId && g.SantiyeId == request.SantiyeId && !g.OdendiMi && g.Tarih < kesimTarihiSiniri)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.OdendiMi, true));

                await _context.Avanslar
                    .Where(a => a.IsciId == request.IsciId && a.SantiyeId == request.SantiyeId && !a.OdendiMi && a.Tarih < kesimTarihiSiniri)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.OdendiMi, true));

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { mesaj = $"Gerçek borç olan {gercekBorcTutari:N2} ₺ başarıyla devredildi ve mühürlendi." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { mesaj = "İşlem sırasında hata oluştu: " + ex.Message });
            }
        }

        public class BorcIadeRequest
        {
            public int IsciId { get; set; }
            public int PatronId { get; set; }
            public int? SantiyeId { get; set; } // 🛡️ ZIRH İÇİN GEREKLİ
            public decimal Tutar { get; set; }
            public string? Ay { get; set; }
            public required string Aciklama { get; set; }
        }



        // 💸 CARİ EKRANINDAN HIZLI ÖDEME (AVANS) MOTORU
        [HttpPost("HizliOdemeYap")]
        public async Task<IActionResult> HizliOdemeYap([FromBody] HizliOdemeRequest request)
        {
            // 🛡️ ZIRH 1: EN TEMEL KONTROL (NullReferenceException Engelleme)
            // Eğer paket hiç gelmediyse veya eksik/hatalı geldiyse DİREKT DURDUR!
            if (request == null || request.IsciId <= 0 || request.PatronId <= 0 || request.Tutar <= 0)
            {
                return BadRequest(new { mesaj = "Geçersiz veya eksik bilgi gönderdiniz." });
            }

            // 🛡️ ZIRH 2: HAYALET AVANS ENGELLEYİCİ (GUARD CLAUSE)
            // Usta'ya para verirken bu paranın hangi inşaatın (şantiyenin) hesabına yazılacağını zorunlu kılıyoruz!
            // (Artık request'in null olmadığını bildiğimiz için güvenle içindeki SantiyeId'ye bakabiliriz)
            if (!request.SantiyeId.HasValue || request.SantiyeId.Value <= 0)
            {
                return BadRequest(new { mesaj = "Hop Patron! Hızlı ödeme yaparken bu paranın HANGİ ŞANTİYENİN masrafına yazılacağını seçmek zorundasın. Şantiyesiz (genel) işçi avansı olmaz!" });
            }

            var isci = await _context.Isciler.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.IsciId);
            if (isci == null) return NotFound(new { mesaj = "İşçi bulunamadı." });

            var patron = await _context.Patronlar.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PatronId);
            if (patron is not { KasaId: int guvenliKasaId })
                return BadRequest(new { mesaj = "Seçilen patronun kasası tanımlı değil!" });

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. PATRONUN KASASINDAN ÇIKACAK GERÇEK TARİH (BUGÜN)
                DateTime gercekIslemZamani = ZamanMotoru.SimdiTurkiye();

                // 2. İŞÇİNİN BORCUNDAN DÜŞÜLECEK HAKEDİŞ TARİHİ
                DateTime isciAvansTarihi = gercekIslemZamani;

                // 🚀 Babanın İsteği: Seçilen ayın 25'ine sabitle!
                if (!string.IsNullOrWhiteSpace(request.HakedisAyi))
                {
                    if (DateTime.TryParseExact(request.HakedisAyi, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var seciliAy))
                    {
                        // Ay kaç çekerse çeksin, işçinin hesabına o ayın 25'i saat 23:59 olarak düşer!
                        isciAvansTarihi = new DateTime(seciliAy.Year, seciliAy.Month, 25, 23, 59, 59);
                    }
                }

                // 🚀 BİRİNCİ DEFTER (İŞÇİNİN DEFTERİ): İşçiye bu parayı GEÇMİŞ aya ait AVANS olarak yazıyoruz
                var yeniAvans = new Avans
                {
                    IsciId = request.IsciId,
                    KasaId = guvenliKasaId,
                    SantiyeId = request.SantiyeId.Value, // 👈 .Value ekledik, tip güvenliği sağlandı!
                    Tutar = request.Tutar,
                    Tarih = isciAvansTarihi, // Ustanın "Seçilen Ayın 25'i" borcu düşer!
                    OdemeTuru = "Nakit (Hızlı Ödeme)",
                    Aciklama = request.Aciklama ?? "hızlı ödeme",
                    OdendiMi = false,
                    IsDeleted = false
                };
                await _context.Avanslar.AddAsync(yeniAvans);

                // 🚀 İKİNCİ DEFTER (PATRONUN KASASI): Parayı BUGÜN çıkıyoruz!
                var kasaHareketi = new KasaHareketi
                {
                    KasaId = guvenliKasaId,
                    PatronId = patron.Id,
                    SantiyeId = request.SantiyeId.Value, // 👈 .Value ekledik, tip güvenliği sağlandı!
                    Tutar = request.Tutar,
                    Yon = KasaIslemYonu.Cikis,
                    HareketTipi = KasaHareketTipi.Avans,
                    IslemTarihi = gercekIslemZamani, // Patronun dökümünde BUGÜN gözükür!
                    Aciklama = $"{isci.Ad} {isci.Soyad} | Hızlı Ödeme. Not: {request.Aciklama}"
                };
                await _context.KasaHareketleri.AddAsync(kasaHareketi);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { mesaj = "Ödeme başarıyla patron kasasından çıkıldı ve işçiye işlendi." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { mesaj = "Ödeme işlemi sırasında hata oluştu: " + ex.Message });
            }
        }// İstek (Request) Modeli
        public class HizliOdemeRequest
        {
            public int IsciId { get; set; }
            public int PatronId { get; set; }
            public int? SantiyeId { get; set; }
            public decimal Tutar { get; set; }
            public string? Aciklama { get; set; }
            public string? HakedisAyi { get; set; } // 🚀 YENİ EKLENDİ
        }

    }
}