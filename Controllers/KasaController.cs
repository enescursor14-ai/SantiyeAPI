using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SantiyeAPI.Services;
using SantiyeAPI.DTOs;
using SantiyeAPI.Data;
using Microsoft.EntityFrameworkCore;
using System;
using SantiyeAPI.Models;
using SantiyeAPI.Exceptions;

namespace SantiyeAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class KasaController : ControllerBase
{
    private readonly IKasaService _kasaService;
    private readonly ILogger<KasaController> _logger;
    private readonly AppDbContext _context;

    public KasaController(IKasaService kasaService, ILogger<KasaController> logger, AppDbContext context)
    {
        _kasaService = kasaService;
        _logger = logger;
        _context = context;
    }

    [HttpPost("avans")]
    public async Task<IActionResult> AvansVer([FromBody] AvansVerRequest request)
    {
        // 🚀 DÜZELTME: request.KasaId yerine request.PatronId logluyoruz
        _logger.LogInformation("Avans verme isteği alındı. İşçi ID: {IsciId}, Patron ID: {PatronId}, Tutar: {Tutar}", request.IsciId, request.PatronId, request.Tutar);

        try
        {
            // 🚀 SENİOR ZIRHI: İşçinin adını veritabanından bul ve açıklamaya mühürle!
            var isci = await _context.Isciler
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == request.IsciId);

            if (isci != null)
            {
                string adSoyad = $"{isci.Ad} {isci.Soyad}".Trim();
                // Eğer not girilmemişse varsayılan bir yazı ata, girilmişse onu kullan
                string girilenNot = string.IsNullOrWhiteSpace(request.Aciklama) ? "Avans Ödemesi" : request.Aciklama.Trim();

                // Açıklamayı ez ve babanın istediği kusursuz formata çevir: "Ali Ramo - Evvv"
                request.Aciklama = $"{adSoyad} - {girilenNot}";
            }

            var avansId = await _kasaService.AvansVerAsync(request);
            return Ok(new { Success = true, Message = "Avans patron kasasından başarıyla çıkıldı.", Data = avansId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Avans verilirken bir hata oluştu.");
            // Frontend'in okuyabileceği formatta hata dönüyoruz
            return BadRequest(new { mesaj = ex.Message });
        }
    }
    [HttpDelete("avans/{avansId}")]
    public async Task<IActionResult> AvansIptalEt(int avansId, [FromQuery] string userId)
    {
        // 🚨 CRITICAL SECURITY WARNING: TEKNİK BORÇ (TECHNICAL DEBT)
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("Avans iptal isteği reddedildi: Kullanıcı ID eksik. Avans ID: {AvansId}", avansId);
            return BadRequest(new { Success = false, Message = "İptal eden kullanıcı ID'si zorunludur!" });
        }

        _logger.LogInformation("Avans iptal isteği alındı. Avans ID: {AvansId}, İptal Eden: {UserId}", avansId, userId);

        await _kasaService.AvansIptalEtAsync(avansId, userId);
        return Ok(new { Success = true, Message = "Avans iptal edildi ve tutar patron kasasına iade edildi." });
    }

    [HttpPost("transfer")]
    public async Task<IActionResult> TransferYap([FromBody] KasaTransferRequest request)
    {
        // 🚀 SENIOR NOTU: Bu metot artık sadece PATRONLAR ARASI borç transferi içindir.
        _logger.LogInformation("Patronlar arası transfer isteği alındı. Gönderen Kasa: {Gonderen}, Alıcı Kasa: {Alici}, Tutar: {Tutar}",
            request.GonderenKasaId, request.AliciKasaId, request.Tutar);

        await _kasaService.KasaTransferYapAsync(request);
        return Ok(new { Success = true, Message = "Patronlar arası kasa transferi başarıyla gerçekleşti." });
    }

    [HttpGet("Ozetler")]
    public async Task<IActionResult> GetKasaOzetleri()
    {
        try
        {
            var ozetler = await (from p in _context.Patronlar
                                 join k in _context.Kasalar on p.KasaId equals k.Id
                                 where p.KasaId != null
                                 select new PatronKasaOzetDto
                                 {
                                     PatronId = p.Id,
                                     PatronAd = (p.Ad + " " + (p.Soyad ?? "")).Trim(),
                                     KasaId = k.Id,

                                     // 🚀 SENIOR MÜHRÜ: Bakiye artık cüzdandan değil, direkt defterden anlık toplanıyor!
                                     // Not: (decimal?) cast işlemi, hiç hareket yoksa (boşsa) null dönmesini engeller ve ?? 0 ile sıfır yazar.
                                     Bakiye = _context.KasaHareketleri
                                                .Where(kh => kh.KasaId == k.Id && !kh.IsDeleted)
                                                .Sum(kh => (decimal?)(kh.Yon == KasaIslemYonu.Giris ? kh.Tutar : -kh.Tutar)) ?? 0m
                                 }).ToListAsync();

            return Ok(ozetler);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mesaj = "Patron kasa özetleri çekilirken hata oluştu: " + ex.Message });
        }
    }

    [HttpPost("sermaye")]
    public async Task<IActionResult> SermayeEkle([FromBody] SermayeEkleRequest request)
    {
        // 🛡️ 1. KAPIDAKİ GÜVENLİK (Validation burada yapılır)
        if (request == null)
            return BadRequest(new { mesaj = "İstek boş olamaz patron!" });

        if (request.Tutar <= 0)
            return BadRequest(new { mesaj = "Sermaye tutarı 0 veya eksi olamaz!" });

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // SantiyeId burada sadece bir ETIKET (Masraf Merkezi) olarak loglanıyor.
        _logger.LogInformation("Sermaye (Para Girişi) isteği alındı. Patron ID: {PatronId}, Hedef Şantiye Etiketi: {SantiyeId}, Tutar: {Tutar}", request.PatronId, request.SantiyeId, request.Tutar);

        try
        {
            // İşi ustasına (Service'e) devrettik
            await _kasaService.SermayeEkleAsync(request);

            return Ok(new
            {
                Success = true,
                Message = "Nakit sermaye patron kasasına başarıyla eklendi."
            });
        }
        catch (BusinessException ex) // Şantiye yoksa veya kasa yoksa buradan yakalar
        {
            return BadRequest(new { mesaj = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sermaye eklenirken hata oluştu.");
            return StatusCode(500, new { mesaj = "Sermaye girişi sırasında kaza çıktı: " + ex.Message });
        }
    }



    [HttpPost("gider")]
    public async Task<IActionResult> ManuelGiderEkle([FromBody] ManuelGiderRequest request)
    {
        _logger.LogInformation("Manuel gider (Masraf) isteği alındı. Patron Kasa ID: {KasaId}, Kategori ID: {KategoriId}, Tutar: {Tutar}",
            request.KasaId, request.KategoriId, request.Tutar);

        try
        {
            // 🛡️ ZIRH EKLENDİ: Servis çağrısı try-catch içine alındı!
            await _kasaService.ManuelGiderEkleAsync(request);

            // 🎯 FRONEND İLE UYUM: 'Message' yerine 'mesaj' dönüyoruz
            return Ok(new { mesaj = "Masraf, patron kasasından başarıyla düşüldü." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manuel gider eklenirken hata oluştu. Kasa ID: {KasaId}", request.KasaId);

            // 🔥 HATA YÖNETİMİ: Frontend'in okuyabileceği 'mesaj' objesi ile hata dönüyoruz!
            return BadRequest(new { mesaj = ex.Message });
        }
    }



    [HttpGet("{kasaId}/bakiye")]
    public async Task<IActionResult> GetBakiye(int kasaId)
    {
        _logger.LogInformation("Patron bakiye sorgulama isteği alındı. Kasa ID: {KasaId}", kasaId);

        var bakiye = await _kasaService.GetKasaBakiyeAsync(kasaId);

        return Ok(new { Success = true, Data = bakiye });
    }
}