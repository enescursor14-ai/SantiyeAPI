using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SantiyeAPI.Data;
using SantiyeAPI.Models;

namespace SantiyeAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AvansController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AvansController(AppDbContext context)
        {
            _context = context;
        }

        // 💵 1. BÜTÜN AVANSLARI GETİR (Kasa Defteri Ekranı İçin)
        // GET: api/Avans?ay=2026-02&santiyeId=1
        [HttpGet]
        public async Task<IActionResult> GetAvanslar([FromQuery] string? ay, [FromQuery] int? santiyeId)
        {
            var query = _context.Avanslar.AsQueryable();

            // 2. Şantiye Filtresi
            if (santiyeId.HasValue && santiyeId.Value > 0)
            {
                query = query.Where(a => a.SantiyeId == santiyeId.Value ||
                                        (a.SantiyeId == null && a.Isci != null && a.Isci.SantiyeIsciler.Any(si => si.SantiyeId == santiyeId.Value && si.AktifMi)));
            }

            // 3. Ay Filtresi
            if (!string.IsNullOrWhiteSpace(ay))
            {
                if (!DateTime.TryParseExact(ay, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var seciliTarih))
                {
                    return BadRequest(new { mesaj = "Hatalı tarih formatı! Lütfen 'YYYY-AA' formatında gönderin." });
                }

                DateTime ayBasi = new DateTime(seciliTarih.Year, seciliTarih.Month, 1);
                DateTime aySonu = ayBasi.AddMonths(1);

                query = query.Where(a => a.Tarih >= ayBasi && a.Tarih < aySonu);
            }

            // 4. Veritabanından Çekim (CS8602 Uyarıları Çözüldü ve Null Zırhları Eklendi)
            var dbListesi = await (from a in query
                                   join p in _context.Patronlar on a.KasaId equals p.KasaId into patronGrup
                                   from patron in patronGrup.DefaultIfEmpty()
                                   orderby a.Tarih descending
                                   select new
                                   {
                                       a.Id,
                                       a.Tarih,
                                       a.Tutar,
                                       a.OdemeTuru,
                                       a.Aciklama,
                                       a.OdendiMi,
                                       a.SantiyeId,

                                       // 🛡️ CS8602 Çözümü: C# derleyicisine null kontrolü yaptığımızı söylüyoruz
                                       AvansSantiyeAd = a.Santiye != null ? a.Santiye.Ad : null,
                                       IsciAd = a.Isci != null ? a.Isci.Ad : null,
                                       IsciSoyad = a.Isci != null ? a.Isci.Soyad : null,
                                       Meslek = a.Isci != null ? a.Isci.Meslek : null,

                                       // 🛡️ İşçi null zırhı ve Şantiye.Ad null filtresi
                                       SantiyeAdlari = a.Isci != null
                                            ? a.Isci.SantiyeIsciler
                                                .Where(si => si.AktifMi && si.Santiye != null && !string.IsNullOrEmpty(si.Santiye.Ad))
                                                .Select(si => si.Santiye!.Ad) // "!" ile derleyiciye bunun null olmadığını garanti ediyoruz
                                                .ToList()
                                            : new List<string>(),

                                       SantiyeIdleri = a.Isci != null
                                            ? a.Isci.SantiyeIsciler
                                                .Where(si => si.AktifMi)
                                                .Select(si => si.SantiyeId)
                                                .ToList()
                                            : new List<int>(),

                                       PatronAd = patron != null ? patron.Ad : null
                                   }).ToListAsync();

            // 5. RAM Üzerinde Formatlama
            var sonuc = dbListesi.Select(a => new
            {
                id = a.Id,
                tarih = a.Tarih.ToString("yyyy-MM-dd"),
                tutar = a.Tutar,
                odemeTuru = a.OdemeTuru,
                aciklama = a.Aciklama,
                odendiMi = a.OdendiMi,

                // 🛡️ İsim Birleştirme İyileştirmesi: Sadece dolu olanları boşlukla birleştir
                isciAdSoyad = (string.IsNullOrWhiteSpace(a.IsciAd) && string.IsNullOrWhiteSpace(a.IsciSoyad))
                    ? "Bilinmeyen Usta"
                    : string.Join(" ", new[] { a.IsciAd, a.IsciSoyad }.Where(s => !string.IsNullOrWhiteSpace(s))),

                meslek = a.Meslek ?? "",

                santiyeAd = a.AvansSantiyeAd ?? (a.SantiyeAdlari.Any() ? string.Join(", ", a.SantiyeAdlari) : "Boşta / Genel Merkez"),
                santiyeId = a.SantiyeId,
                santiyeIdleri = a.SantiyeIdleri,
                patronAd = a.PatronAd ?? "Şirket"
            }).ToList();

            return Ok(sonuc);
        }
    }
}