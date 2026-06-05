using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SantiyeAPI.Data;
using SantiyeAPI.Models;
using System.ComponentModel.DataAnnotations;
using SantiyeAPI.Helpers;
using Microsoft.Extensions.Caching.Memory;
using SantiyeAPI.DTOs;


namespace SantiyeAPI.Controllers
{
    public class PatronCreateDto
    {
        [Required(ErrorMessage = "Patron adı boş olamaz!")]
        public string Ad { get; set; } = string.Empty;

        [Required(ErrorMessage = "Patron soyadı boş olamaz!")]
        public string Soyad { get; set; } = string.Empty;

        [Required(ErrorMessage = "Telefon numarası 11 hane olmalıdır!")]
        [StringLength(11, MinimumLength = 11)]
        public string Telefon { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ünvan boş olamaz!")]
        public string Unvan { get; set; } = string.Empty;
    }

    [Route("api/[controller]")]
    [ApiController]
    public class PatronController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly ILogger<PatronController> _logger;

        public PatronController(AppDbContext context, IMemoryCache cache, ILogger<PatronController> logger)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> YeniPatronEkle([FromBody] PatronCreateDto dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                dto.Ad = StringHelper.ToTitleCaseTr(dto.Ad);
                dto.Soyad = StringHelper.ToTitleCaseTr(dto.Soyad);
                dto.Unvan = StringHelper.ToTitleCaseTr(dto.Unvan);
                var yeniKasa = new Kasa
                {
                    Ad = $"{dto.Ad} {dto.Soyad} Kasası"
                    // 🗑️ MÜHÜR 2: Bakiye = 0 satırını tamamen uçurduk.
                };

                _context.Kasalar.Add(yeniKasa);
                await _context.SaveChangesAsync();

                var yeniPatron = new Patron
                {
                    Ad = dto.Ad,
                    Soyad = dto.Soyad,
                    Telefon = dto.Telefon,
                    Unvan = dto.Unvan,
                    KasaId = yeniKasa.Id
                };

                _context.Patronlar.Add(yeniPatron);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return Ok(new { mesaj = $"{dto.Ad} {dto.Soyad} için kasa açıldı ve ortaklık tanımlandı." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { mesaj = "İşlem başarısız: " + ex.Message });
            }
        }
        [HttpPut("{id}")]
        public async Task<IActionResult> PatronGuncelle(int id, [FromBody] PatronUpdateDto dto)
        {
            var patron = await _context.Patronlar.FindAsync(id);
            if (patron == null) return NotFound(new { mesaj = "Patron bulunamadı." });

            if (dto.SorumluOlduguSantiyeId.HasValue)
            {
                var santiyeVarMi = await _context.Santiyeler.AnyAsync(s => s.Id == dto.SorumluOlduguSantiyeId);
                if (!santiyeVarMi) return BadRequest(new { mesaj = "Seçilen şantiye sistemde bulunamadı!" });
            }

            patron.Ad = StringHelper.ToTitleCaseTr(dto.Ad);
            patron.Soyad = StringHelper.ToTitleCaseTr(dto.Soyad);
            patron.Telefon = dto.Telefon;
            patron.Unvan = StringHelper.ToTitleCaseTr(dto.Unvan);
            patron.SorumluOlduguSantiyeId = dto.SorumluOlduguSantiyeId;

            await _context.SaveChangesAsync();
            return Ok(new { mesaj = "Bilgiler başarıyla güncellendi." });
        }

        // 🚀 KRİTİK SATIR BURASI: Bu öznitelik olmazsa 405 hatası alırsın!
        [HttpGet("Liste")]
        public async Task<IActionResult> GetPatronlar()
        {
            try
            {
                _logger.LogInformation("Patron listesi ve canlı bakiyeler veritabanından anlık çekiliyor...");

                // ✅ 1. SORGU: Tüm kasa bakiyelerini tek seferde topla
                var kasaBakiyeleri = await _context.KasaHareketleri
                    .Where(kh => !kh.IsDeleted)
                    .GroupBy(kh => kh.KasaId)
                    .Select(g => new
                    {
                        KasaId = g.Key,
                        Bakiye = g.Sum(kh => kh.Yon == KasaIslemYonu.Giris ? kh.Tutar : -kh.Tutar)
                    })
                    .ToDictionaryAsync(x => x.KasaId, x => x.Bakiye);

                // ✅ 2. SORGU: Patronları çek, bakiyeyi dictionary'den al
                var patronlar = await _context.Patronlar
                    .AsNoTracking()
                    .Select(p => new PatronListDto
                    {
                        Id = p.Id,
                        Ad = p.Ad,
                        Soyad = p.Soyad,
                        Telefon = p.Telefon,
                        Unvan = p.Unvan,
                        KasaId = p.KasaId,
                        SorumluOlduguSantiyeId = p.SorumluOlduguSantiyeId,
                        SantiyeAd = p.SorumluOlduguSantiye != null ? p.SorumluOlduguSantiye.Ad : "Atanmamış",
                        Bakiye = p.KasaId.HasValue
                            ? kasaBakiyeleri.GetValueOrDefault(p.KasaId.Value, 0m)
                            : 0m
                    })
                    .ToListAsync();

                return Ok(patronlar);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Patron listesi çekilirken hata oluştu.");
                return StatusCode(500, new { mesaj = "Patronlar listesi alırken hata: " + ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetPatron(int id)
        {
            try
            {
                var patron = await _context.Patronlar
                    .AsNoTracking()
                    .Where(p => p.Id == id)
                    .Select(p => new
                    {
                        p.Id,
                        p.Ad,
                        p.Soyad,
                        p.KasaId,
                        p.Telefon,
                        p.Unvan,
                        p.SorumluOlduguSantiyeId,
                        SantiyeAd = p.SorumluOlduguSantiye != null ? p.SorumluOlduguSantiye.Ad : "Atanmamış"
                    })
                    .FirstOrDefaultAsync();

                if (patron == null)
                    return NotFound(new { mesaj = "Patron bulunamadı." });

                decimal bakiye = 0;
                if (patron.KasaId.HasValue)
                {
                    bakiye = await _context.KasaHareketleri
                        .Where(kh => kh.KasaId == patron.KasaId && !kh.IsDeleted)
                        .SumAsync(kh => kh.Yon == KasaIslemYonu.Giris ? kh.Tutar : -kh.Tutar);
                }

                return Ok(new
                {
                    patron.Id,
                    patron.Ad,
                    patron.Soyad,
                    patron.KasaId,
                    patron.Telefon,
                    patron.Unvan,
                    patron.SorumluOlduguSantiyeId,
                    patron.SantiyeAd,
                    Bakiye = bakiye
                });
            }
            catch (Exception)
            {
                return StatusCode(500, new { mesaj = "Sunucu hatası oluştu." });
            }
        }

        // 🚀 ZIRH: Döküm ekranı artık sadece son sıfırlamadan (MİLAT) sonrasını getiriyor!
        [HttpGet("{patronId}/hareketler")]
        public async Task<IActionResult> GetPatronHareketleri(int patronId, [FromQuery] bool tumunuGetir = false)
        {
            DateTime milatTarihi = DateTime.MinValue;

            // Eğer "Tüm Geçmişi Göster" tuşuna basılmadıysa, son sıfırlama tarihini bul
            if (!tumunuGetir)
            {
                var sonSifirlama = await _context.KasaHareketleri
                    .AsNoTracking()
                    .Where(h => h.PatronId == patronId && !h.IsDeleted && h.SifirlamaFisiMi)
                    .MaxAsync(h => (DateTime?)h.IslemTarihi);

                if (sonSifirlama.HasValue)
                {
                    milatTarihi = sonSifirlama.Value;
                }
            }

            var hareketler = await _context.KasaHareketleri
                .Include(k => k.Santiye)
                .Where(k => k.PatronId == patronId && !k.IsDeleted && !k.SifirlamaFisiMi && k.IslemTarihi > milatTarihi)
                .OrderByDescending(k => k.IslemTarihi)
                .Select(k => new
                {
                    k.Id,
                    Tarih = k.IslemTarihi.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    k.Tutar,
                    k.Yon,
                    k.HareketTipi,
                    k.Aciklama,
                    SantiyeAd = k.Santiye != null ? k.Santiye.Ad : "Genel Merkez"
                })
                .ToListAsync();

            return Ok(hareketler);
        }



        // Düzenleme için ayrı bir DTO (veya mevcut olanı kullanabilirsin)
        public class PatronUpdateDto : PatronCreateDto
        {
            public int? SorumluOlduguSantiyeId { get; set; }
        }

        /*[HttpDelete("{id}")]
        public async Task<IActionResult> PatronSil(int id)
        {
            var patron = await _context.Patronlar
                .Include(p => p.Kasa)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (patron == null)
                return NotFound(new { mesaj = "Patron bulunamadı." });

            var hareketVarMi = await _context.KasaHareketleri.AnyAsync(h => h.PatronId == id);
            if (hareketVarMi)
                return BadRequest(new { mesaj = "Bu ortağın üzerinde aktif/geçmiş kasa hareketleri var. Kayıtları bozmamak için silemezsiniz!" });

            if (patron.Kasa != null)
                _context.Kasalar.Remove(patron.Kasa);

            _context.Patronlar.Remove(patron);
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Ortak ve bağlı kasası başarıyla temizlendi." });
        }
        */
    }

}